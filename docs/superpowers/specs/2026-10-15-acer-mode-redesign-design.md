# Acer Mode 2 — redesign e suíte de iluminação

> **Status:** design aprovado em conversa (2026-10-15), aguardando revisão desta spec.
> **Máquina-alvo:** Acer Predator Helios Neo 16 (PHN16-72), Windows 11.
> **Base:** commit `6b1f46b` (app WinForms de arquivo único, `AcerMode.cs`).

## 1. Objetivo

Substituir o PredatorSense por um utilitário próprio, bonito e completo. O Acer Mode
atual troca o perfil térmico e pinta o teclado com uma cor fixa por modo, mas:

- a interface é uma skin de PNGs desenhada à mão em GDI+ na CPU, rígida e com cara de
  template;
- não existe forma de escolher cor, efeito ou brilho do teclado;
- não mostra temperatura nem rotação das ventoinhas, e não controla as ventoinhas.

**Pronto significa:** as cinco áreas da §5 funcionando no PHN16-72 real, com as animações
da §6, sem regressão nas funções que já existem (troca de modo, botão físico, OSD,
bandeja, iniciar com o Windows, CLI).

## 2. Escopo

**Dentro:**

- reescrita da interface em WPF (.NET 8), com janela de trilho lateral **e** flyout da bandeja;
- suíte de iluminação do teclado de 4 zonas: cor por zona, efeitos, brilho, velocidade, direção;
- perfis por modo térmico (iluminação + ventoinhas), editáveis e persistidos;
- painel de temperaturas de CPU/GPU e RPM das ventoinhas;
- controle de ventoinhas Auto / Máximo / Personalizado (% fixo), com proteção (`FanGuard`);
- manutenção de tudo que já existe: bandeja, OSD, botão físico (alterna Turbo),
  autostart via Agendador de Tarefas, CLI `--set` / `--status` / `--tray` / `--shot`.

**Fora (YAGNI, revisitar só se pedido):**

- perfis de overclock de CPU (`SetCPUOverclockingProfile`);
- editor de curva de ventoinha (`SetGamingFanTable`);
- LEDs de tampa/logo (`SetGamingLED*`);
- trocar o comportamento do botão físico;
- qualquer dado saindo da máquina (telemetria, atualização automática).

## 3. Protocolo de hardware

Toda a comunicação é pela classe WMI `root\WMI:AcerGamingFunction`. O app exige
administrador (`app.manifest`, `requireAdministrator`), como hoje.

### 3.1 Confirmado no PHN16-72 (em uso no app atual)

| Função | Chamada | Formato |
|---|---|---|
| Ler modo térmico | `GetGamingMiscSetting(gmInput = 0x0B)` | modo = `(gmOutput >> 8) & 0xFF` |
| Definir modo térmico | `SetGamingMiscSetting(gmInput = code << 8 \| 0x0B)` | sucesso se `gmOutput & 0xFF == 0` |
| Códigos de modo | — | Quiet `0`, Balanced `1`, Performance `4`, Turbo `5`, Eco `6` |
| Cor de uma zona | `SetGamingRgbKb(gmInput)` | `zonaBitmask \| R << 8 \| G << 16 \| B << 24`, zonas `1,2,4,8` |
| Efeito/brilho | `SetGamingKBBacklight(gmInput = byte[16])` | `[efeito, velocidade, brilho, 0, direção, R, G, B, 3, 1, 0…]`; `3` = motor de 4 zonas |
| Botão físico | evento `APGeEvent` | `EventDetail` começando com `07 05 01` |

O efeito `0` (estático) com cores por zona é o único comportamento de iluminação já
validado. Os eventos `08 …` e `09 …` também chegam pelo `APGeEvent` e hoje são ignorados.
Não se sabe o que significam, e eles continuam ignorados (o novo log de eventos registra
cada um para investigação futura).

### 3.2 Hipóteses (a confirmar na Fase 0, antes de qualquer UI depender delas)

Estes valores vêm de drivers comunitários para outros modelos Predator e **não** foram
verificados neste notebook:

- **Efeitos** no byte 0 de `SetGamingKBBacklight`: estático, respirar, neon, onda,
  alternar, zoom, meteoro, cintilar (códigos `0–7`); velocidade `0–9`; brilho `0–100`;
  direção `1`/`2`. Efeitos animados usam uma cor única (ou arco-íris, conforme o efeito).
- **Sensores** em `GetGamingSysInfo`: temperatura de CPU e GPU e RPM das duas ventoinhas,
  cada um selecionado por um código no `gmInput`.
- **Ventoinhas**: `SetGamingFanBehavior` escolhe Auto / Máximo / Personalizado por
  ventoinha (CPU e GPU), e `SetGamingFanSpeed` define o % no modo Personalizado;
  `Get*` correspondentes leem o estado.

**Regra:** o que a Fase 0 não confirmar fica **oculto** na interface. Não se entrega
controle meio funcionando.

## 4. Arquitetura

Solução .NET 8 em `C:\Users\pedro\AcerMode`, substituindo o `AcerMode.cs` de arquivo
único (que permanece no histórico do git).

```
AcerMode.sln
src/
  AcerMode.Core/      biblioteca sem UI (net8.0-windows)
  AcerMode.App/       WPF (net8.0-windows), CommunityToolkit.Mvvm
tests/
  AcerMode.Tests/     xUnit
docs/superpowers/specs/
```

### 4.1 `AcerMode.Core`

Cada unidade tem uma responsabilidade e é testável sem o notebook.

| Unidade | Faz | Depende de |
|---|---|---|
| `IAcerHardware` | Contrato de alto nível: ler/definir modo, aplicar iluminação, ler sensores, ler/definir ventoinhas, evento do botão físico | — |
| `AcerWmi : IAcerHardware` | Única classe que toca `AcerGamingFunction`. Todas as chamadas passam por **uma** thread trabalhadora serializada, com timeout de 4 s e pausa de 30 s após falha (a lógica atual de `Bios.WithTimeout`) | `Encoders`, `System.Management` |
| `FakeHardware : IAcerHardware` | Estado em memória com sensores simulados; usado em `--demo`, capturas e testes de UI | — |
| `Encoders` | Funções **puras** domínio ↔ payload: modo, cor por zona, `byte[16]` de efeito, comportamento/% de ventoinha, parsing de sensores | — |
| `Profiles` | Carrega, valida, migra e salva `settings.json`; produz os padrões (as cores atuais por modo) | `System.Text.Json` |
| `FanGuard` | Regras de segurança das ventoinhas (§7.2); recebe leituras, devolve ações | — |
| `Sensors` | Faz polling de 1 s **apenas** enquanto janela ou flyout estiverem visíveis; expõe as últimas 120 amostras | `IAcerHardware` |
| `HardwareKey` | Watcher do `APGeEvent`, com debounce de 600 ms (lógica atual) | `System.Management` |
| `Autostart` | Tarefa agendada no logon, igual à atual (inclusive rodar na bateria) | `schtasks.exe` |
| `EventLog` | Log rotativo em `%APPDATA%\AcerMode\events.log` (substitui o `key.log` ao lado do exe) | — |

**Orquestração:** um `ModeService` aplica uma troca de modo como uma transação:
modo térmico → iluminação do perfil → ventoinhas do perfil. Se a troca de modo falhar,
nada mais é aplicado. Se iluminação ou ventoinha falharem, o modo fica trocado e o
erro aparece na interface (a cor do teclado nunca impede a troca de modo, como hoje).

### 4.2 `AcerMode.App`

- **Páginas** como `UserControl` compartilhados (Modos, Iluminação, Sistema, Ventoinhas,
  Ajustes), cada uma com seu ViewModel.
- **Dois shells:**
  - `ShellWindow`: janela de trilho lateral (~860×560), barra de título própria;
  - `TrayFlyout`: popover de ~380×620 ancorado acima da bandeja, aberto com clique
    esquerdo no ícone; fecha ao perder o foco.
- `OsdWindow` (sem foco, transparente ao mouse, fora do Alt-Tab), menu de contexto da
  bandeja, instância única (`Local\AcerModeSingle` + evento `Local\AcerModeShow`).
- `--demo` injeta `FakeHardware`; `--shot <página> <arquivo.png>` renderiza uma página
  fora da tela para conferência visual.

### 4.3 CLI mantida e nova

| Argumento | Comportamento |
|---|---|
| `--tray` | Inicia escondido na bandeja (usado pelo autostart) |
| `--set <modo>` / `--status` | Igual ao atual, escrevendo em `last-status.txt` |
| `--shot …` | Captura de página/OSD sem janela |
| `--demo` | UI completa sobre `FakeHardware` |
| `--probe` | **Novo.** Só leitura: chama todos os `Get*` com as entradas candidatas e grava as respostas cruas em `probe-<data>.txt` |

## 5. Páginas e interação

### 5.1 Modos
- Topo: modo ativo em destaque com CPU/GPU °C ao vivo.
- Cinco cartões: ícone, nome, descrição curta (“Silencioso, ventoinhas baixas”) e uma
  faixa com a iluminação do perfil daquele modo.
- Um clique aplica a transação do `ModeService` e dispara o OSD.

### 5.2 Iluminação
- Seletor “Editando perfil:” (padrão = modo ativo).
- Teclado desenhado com 4 zonas clicáveis; seleção de uma zona ou chave “Todas as zonas”.
- Chips de efeito: somente os confirmados na Fase 0. Cores por zona só no Estático;
  nos demais, uma cor (ou nenhuma, para efeitos arco-íris).
- Cor: amostras predefinidas, roda HSV e campo hex.
- Sliders: brilho sempre; velocidade e direção aparecem só para efeitos que as usam.
- Pré-visualização ao vivo: se o perfil editado é o do modo ativo, cada mudança vai
  ao teclado com debounce de ~120 ms.
- Chave “Iluminação independente do modo”: um único perfil global, que não muda ao
  trocar de modo.

### 5.3 Sistema
- Medidores em arco para CPU e GPU (°C), RPM das duas ventoinhas, sparkline de 2 min.
- Somente leitura.

### 5.4 Ventoinhas
- Por modo: Auto / Máximo / Personalizado.
- Personalizado: sliders separados de CPU e GPU, mínimo de 30 %.
- Linha de status do `FanGuard` (“Proteção ativou às 14:02: CPU a 87 °C, voltou ao Auto”).

### 5.5 Ajustes
- Iniciar com o Windows, mostrar OSD, restaurar perfis padrão, visualizar log de eventos.

### 5.6 Flyout
- Linha com os 5 modos em botões compactos.
- Iluminação atual: efeito, amostras, brilho.
- Linha de temperaturas e botão “Abrir Acer Mode”.

### 5.7 Mantidos
- Menu de contexto da bandeja (modos, abrir, iniciar com o Windows, sair).
- Ícone da bandeja com selo na cor do modo ativo.
- Botão físico alterna Turbo ↔ modo anterior.
- OSD a cada troca de modo.

## 6. Tema e movimento

### 6.1 Tema
- Base quase preta com Mica (janela) e acrílico (flyout); superfícies planas em 3 níveis
  de elevação; bordas de 1 px com 8–10 % de branco.
- Tipografia Segoe UI Variable; ícones Fluent System Icons (vetoriais), no lugar dos PNGs
  gerados.
- Um único recurso `ModeAccent` dirige toda a cor de destaque:

  | Modo | Destaque |
  |---|---|
  | Quiet | ciano |
  | Balanced | azul |
  | Performance | violeta |
  | Turbo | vermelho-laranja |
  | Eco | verde |

- Brilho (glow) só no cartão do modo ativo e na zona em edição.
- Nada rasterizado: tudo vetorial, nítido em qualquer DPI.

### 6.2 Movimento
Linguagem única: 150–250 ms, ease-out; molas só onde algo “pousa”.

| Momento | Animação |
|---|---|
| Troca de modo | `ModeAccent` faz cross-fade na interface inteira (300 ms); ripple a partir do cartão clicado; números de temperatura contam até o valor; prévia do teclado varre as zonas da esquerda para a direita na nova cor |
| Navegação | página desliza 12 px + fade; indicador do trilho desliza até o item |
| Abrir janela | fade + subida de 14 px (como hoje) |
| Abrir flyout | cresce a partir do canto da bandeja |
| Controles | hover clareia a borda e escala 1,01; clique afunda para 0,98; amostra selecionada dá um “pop” |
| Dados ao vivo | medidores e sparklines interpolam entre leituras |
| Turbo | pulso lento de “respiração” no destaque |
| OSD | mesmo pop/fade atual, redesenhado em vetor com anel na cor do modo |

- Com “Mostrar animações” desligado no Windows, tudo vira fade instantâneo.
- Animações só rodam com algo visível. Na bandeja, CPU ~0 % e polling de sensores parado.

## 7. Erros e segurança

### 7.1 Erros
- **BIOS sem resposta:** timeout + pausa atuais; faixa não bloqueante
  “A BIOS não respondeu, tentando de novo”.
- **Retorno da suspensão:** reaplica o perfil do modo ativo (o firmware às vezes perde a
  iluminação).
- **`settings.json` corrompido:** renomeado para `settings.bad-<data>.json` e substituído
  pelos padrões, com aviso.
- **Escrita de ventoinha:** sempre seguida de leitura de confirmação; divergência vira
  erro visível e volta para Auto.
- Mensagens em PT-BR, dizendo o que houve e o que fazer; nunca a exceção crua.

### 7.2 `FanGuard`
- Ativo só quando alguma ventoinha está em Personalizado.
- CPU ou GPU **> 85 °C** → aquele modo volta a Auto imediatamente, com registro no log
  e aviso na interface. O Personalizado só é reativado manualmente.
- Saída normal do app → ventoinhas em Personalizado voltam a Auto.
- Crash: um marcador `fans-dirty` é gravado ao entrar em Personalizado e apagado na
  saída limpa; se existir na próxima inicialização, volta a Auto antes de qualquer outra coisa.
- Sensores indisponíveis → Personalizado fica desabilitado (sem leitura, sem proteção).

## 8. Dados persistidos

`%APPDATA%\AcerMode\settings.json`:

```json
{
  "version": 1,
  "independentLighting": false,
  "globalLighting": null,
  "showOsd": true,
  "profiles": {
    "Turbo": {
      "lighting": {
        "effect": "Static",
        "zones": ["#FF2D0A", "#FF2D0A", "#FF2D0A", "#FF2D0A"],
        "color": "#FF2D0A",
        "brightness": 100,
        "speed": 5,
        "direction": "RightToLeft"
      },
      "fans": { "mode": "Auto", "cpuPercent": 60, "gpuPercent": 60 }
    }
  }
}
```

- Um perfil para cada um dos 5 modos.
- `zones` vale só para o efeito Estático; `color` vale para os efeitos animados de cor
  única. Os dois são guardados para que trocar de efeito e voltar não perca a escolha.
- Ao ligar `independentLighting`, `globalLighting` nasce como cópia da iluminação do modo
  ativo; ao desligar, ele é mantido no arquivo, mas ignorado.
- Padrões: Turbo `#FF2D0A`/100, Performance `#9646FF`/100, Quiet `#00BEFF`/45,
  Eco `#28DC5A`/70, Balanced `#2878FF`/100 (as cores do app atual), efeito Estático,
  ventoinhas Auto.
- `version` permite migração; campos desconhecidos são preservados.

## 9. Testes e critérios de pronto

- **Unitários (xUnit):**
  - `Encoders` com payloads “golden”, incluindo os confirmados da §3.1 (ex.: Turbo
    `0x050B`; zona 1 vermelha `0x0000FF01`);
  - migração/validação de `Profiles`;
  - todas as regras do `FanGuard`;
  - transação do `ModeService` sobre `FakeHardware`.
- **Visual:** `--demo` + `--shot` de cada página e estado (cada modo, cada efeito,
  erro, proteção ativada).
- **Máquina real (checklist final):**
  - trocar os 5 modos pela janela, pelo flyout, pela bandeja e pelo botão físico;
  - cada efeito confirmado;
  - Auto, Máximo e Personalizado nas duas ventoinhas, conferindo RPM;
  - forçar o `FanGuard` (limite reduzido temporariamente em build de teste);
  - suspender e retomar;
  - reiniciar com autostart ligado;
  - CPU ociosa na bandeja.

## 10. Fases de entrega

| Fase | Entrega | Gate |
|---|---|---|
| 0 — Probe | `--probe` (somente leitura) + escritas guiadas, uma por vez, com sua autorização e caminho de volta conhecido. Resultado registrado nesta spec (§3.2 → §3.1) | Lista do que foi confirmado |
| 1 — Core | Solução, `Encoders`, `Profiles`, `FanGuard`, `AcerWmi`, `FakeHardware`, testes | Testes verdes |
| 2 — Janela + Modos | `ShellWindow`, tema, `ModeAccent`, página Modos, animação de troca | Captura + teste na máquina |
| 3 — Iluminação | Página completa com prévia ao vivo | Cada efeito confirmado no teclado |
| 4 — Sistema + Ventoinhas | Sensores, medidores, controle de ventoinhas, `FanGuard` ligado | Checklist de ventoinhas |
| 5 — Flyout, OSD, bandeja | Flyout, OSD vetorial, menu, instância única, migração do autostart para o novo exe | Checklist completo da §9 |

O exe antigo e a tarefa agendada antiga só são substituídos ao fim da Fase 5.
