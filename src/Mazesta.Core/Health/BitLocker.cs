namespace Mazesta.Core.Health;

/// <summary>What BitLocker is doing to a volume. Every state but <see cref="Off"/> and <see cref="Unknown"/> means its data is encrypted, or is being
/// encrypted or decrypted, and can be locked behind the recovery key: after a Windows reinstall, a BIOS or firmware update, a TPM reset or a new board.</summary>
public enum BitLockerState { Unknown, Off, On, Suspended, WaitingForActivation, Encrypting, Decrypting }

/// <summary>
/// One volume as Win32_EncryptableVolume reports it: ProtectionStatus (0 off, 1 on, 2 locked), ConversionStatus (0 decrypted, 1 encrypted,
/// 2 encrypting, 3 decrypting, 4 encryption paused, 5 decryption paused), and its key protectors. A figure Windows did not give is null and makes
/// the state Unknown, never Off: a volume that could not be read may still be encrypted.
/// </summary>
/// <param name="Protectors">How many key protectors it has (TPM, password, recovery password...); none on an encrypted volume is device encryption
/// "waiting for activation", whose key sits in the clear until a Microsoft account (or a TPM protector) takes it.</param>
/// <param name="RecoveryPassword">Whether it has a 48-digit recovery password; an encrypted volume without one cannot be opened if it locks.</param>
public sealed record BitLockerVolume(string Letter, int? Protection, int? Conversion, int? Protectors, bool? RecoveryPassword)
{
    public BitLockerState State => (Protection, Conversion) switch
    {
        (null, _) or (_, null) => BitLockerState.Unknown,
        (_, 3 or 5) => BitLockerState.Decrypting,
        (0, 0) => BitLockerState.Off,
        (0, 1 or 2 or 4) when Protectors == 0 => BitLockerState.WaitingForActivation,
        (_, 2 or 4) => BitLockerState.Encrypting,
        (1 or 2, _) => BitLockerState.On,
        (0, 1) => BitLockerState.Suspended,
        _ => BitLockerState.Unknown,
    };

    /// <summary>Whether a reinstall or a firmware update can lock this volume's data behind its recovery key.</summary>
    public bool CanLock => State is not (BitLockerState.Off or BitLockerState.Unknown);
}
