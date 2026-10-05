namespace Mazesta.Web;

/// <summary>
/// The public half of the shop's update-signing key (ECDSA P-256, SubjectPublicKeyInfo in Base64), made on 2026-10-06 with
/// <c>mazesta-release keygen</c> (the first key, of 2026-09-29, was replaced the same day: no release had been handed out with it). The private half is
/// artifacts\Mazesta-Update\Keys\mazesta-update-private.pem on the owner's machine (artifacts/ is not committed) and is never committed.
/// Changing this key means every copy already installed refuses updates signed with the new one: they must be updated by hand once.
/// </summary>
internal static class UpdateKey
{
    public const string Public = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEp6D92VN21lZEtbH0WWSHhNN2k0rpaK0WSao3vp2l61u5LyGFgx0C0I2zY/xpu+av+BMolzjOb3e+5Lc2IWjZXg==";
}
