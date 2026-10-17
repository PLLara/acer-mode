using AcerMode.Core;
using Xunit;

public class FastTempsTests
{
    // Monta um CORE_TEMP_SHARED_DATA mínimo: uiLoad[256], uiTjMax[128], uiCoreCnt, uiCPUCnt,
    // fTemp[256], fVID, fCPUSpeed, fFSBSpeed, fMultiplier, sCPUName[100], ucFahrenheit, ucDeltaToTjMax.
    static byte[] Shared(float[] temps, uint tjMax = 100, byte fahrenheit = 0, byte delta = 0)
    {
        var b = new byte[CoreTempLayout.Size];
        BitConverter.GetBytes(tjMax).CopyTo(b, CoreTempLayout.TjMax);
        BitConverter.GetBytes((uint)temps.Length).CopyTo(b, CoreTempLayout.CoreCount);
        BitConverter.GetBytes(1u).CopyTo(b, CoreTempLayout.CpuCount);
        for (int i = 0; i < temps.Length; i++) BitConverter.GetBytes(temps[i]).CopyTo(b, CoreTempLayout.Temps + 4 * i);
        b[CoreTempLayout.Fahrenheit] = fahrenheit;
        b[CoreTempLayout.DeltaToTjMax] = delta;
        return b;
    }

    [Fact] public void HottestCore_Celsius() => Assert.Equal(70, CoreTempLayout.HottestC(Shared(new[] { 60f, 70.4f, 66f })));
    [Fact] public void Fahrenheit_IsConverted() => Assert.Equal(90, CoreTempLayout.HottestC(Shared(new[] { 194f }, fahrenheit: 1)));
    [Fact] public void DeltaToTjMax_IsConverted() => Assert.Equal(85, CoreTempLayout.HottestC(Shared(new[] { 15f, 30f }, tjMax: 100, delta: 1)));
    [Fact] public void NoCores_IsNull() => Assert.Null(CoreTempLayout.HottestC(Shared(Array.Empty<float>())));
    [Fact] public void ImplausibleValue_IsNull() => Assert.Null(CoreTempLayout.HottestC(Shared(new[] { 0f })));
    [Fact] public void TooSmallBuffer_IsNull() => Assert.Null(CoreTempLayout.HottestC(new byte[16]));
}
