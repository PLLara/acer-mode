# Acer Mode 2 — verificação (spec §9)

Data: 2026-10-15. Build publicado: `AcerMode.exe` 2.0.0 (1,3 MB, framework-dependent,
arquivo único), em `C:\Users\pedro\AcerMode\AcerMode.exe`. O exe antigo ficou como
`AcerMode.legacy.exe`; para voltar, basta renomear os dois.

## Verificado nesta sessão

| Item | Como | Resultado |
|---|---|---|
| Testes do Core | `dotnet test tests/AcerMode.Tests` | 79/79 |
| Leitura de sensores, ventoinhas e cor | `--probe` manual elevado (spec §3.1) | confirmado |
| Escrita dos 8 efeitos | `tools/verify-writes.ps1` | status 0 em todos |
| Ventoinhas Auto / Máximo / Personalizado | `tools/verify-writes.ps1` | leitura de volta correta; RPM respondeu |
| Todas as páginas nos 5 modos, flyout e OSD | `--shot` / `--shot-osd` em `design/v2/` (35 imagens) | sem texto cortado; tema acompanha o modo |
| App de demonstração | `--demo` por 6 s | sem crash |
| App real elevado | troca do exe + `--tray` | iniciou, watcher do botão físico ativo |
| Tarefa agendada | `schtasks /Query /TN AcerMode` | aponta para o mesmo caminho com `--tray` |
| CLI falhando sai do processo | `--shot` com caminho inválido | sai com código 1 em menos de 20 s |

## Pendente (precisa de alguém no notebook)

- [ ] Trocar os 5 modos pela janela, pelo flyout, pelo menu da bandeja e pelo botão físico, conferindo o OSD.
- [ ] Ver cada efeito de teclado (Respirar, Neon, Onda, Alternar, Zoom, Meteoro, Cintilar) e retirar de `KbEffects.Supported` o que não mudar nada visível.
- [ ] Prévia ao vivo: arrastar a roda de cor com o perfil ativo e ver o teclado acompanhar.
- [ ] Personalizado 40/40 em Turbo sob carga: o `FanGuard` deve voltar ao automático e avisar.
- [ ] Suspender e retomar: iluminação reaplicada em ~3 s.
- [ ] Reiniciar com "Iniciar com o Windows" ligado: abre na bandeja sem UAC.
- [ ] Gerenciador de Tarefas: ~0 % de CPU depois de 1 min com a janela escondida.
