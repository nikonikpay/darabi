using Xunit; using Mazesta.Core.Health;
namespace Mazesta.Core.Tests;

public class BitLockerTests
{
    // Win32_EncryptableVolume: ProtectionStatus 0 off / 1 on / 2 locked; ConversionStatus 0 decrypted, 1 encrypted, 2 encrypting, 3 decrypting, 4 and 5 paused.
    [Theory]
    [InlineData(0, 0, 0, BitLockerState.Off), InlineData(1, 1, 2, BitLockerState.On), InlineData(2, 1, 2, BitLockerState.On)]
    [InlineData(0, 1, 2, BitLockerState.Suspended), InlineData(0, 1, 0, BitLockerState.WaitingForActivation), InlineData(0, 2, 0, BitLockerState.WaitingForActivation)]
    [InlineData(1, 2, 2, BitLockerState.Encrypting), InlineData(0, 4, 1, BitLockerState.Encrypting), InlineData(0, 3, 2, BitLockerState.Decrypting), InlineData(1, 5, 2, BitLockerState.Decrypting)]
    public void Each_state_is_told_apart(int protection, int conversion, int protectors, BitLockerState expected) => Assert.Equal(expected, new BitLockerVolume("C:", protection, conversion, protectors, true).State);

    [Fact] public void An_unread_figure_is_unknown_never_off_and_only_unknown_and_off_cannot_lock()
    {
        Assert.Equal(BitLockerState.Unknown, new BitLockerVolume("C:", null, 0, 0, null).State); Assert.Equal(BitLockerState.Unknown, new BitLockerVolume("C:", 0, null, 0, null).State);
        Assert.False(new BitLockerVolume("C:", null, 1, 0, null).CanLock); Assert.False(new BitLockerVolume("C:", 0, 0, 0, null).CanLock);
        Assert.True(new BitLockerVolume("C:", 0, 1, null, null).CanLock);   // protectors unread: encrypted all the same, so it can lock
    }
}
