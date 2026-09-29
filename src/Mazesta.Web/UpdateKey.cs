namespace Mazesta.Web;

/// <summary>
/// The public half of the shop's update-signing key (ECDSA P-256, SubjectPublicKeyInfo in Base64), made on 2026-09-29 with
/// <c>mazesta-release keygen</c>. The private half is G:\Mazesta-Keys\mazesta-update-private.pem on the owner's machine and is never committed.
/// Changing this key means every copy already installed refuses updates signed with the new one: they must be updated by hand once.
/// </summary>
internal static class UpdateKey
{
    public const string Public = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEMjJtu8uKvbGN7vM/lRR7NPNuQbqSKbkDQBv+d4a91tpZinD6xGsuy0cha6nd7n3Iizkle78ZkhxWA221BI6gnQ==";
}
