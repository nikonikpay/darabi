using Xunit;
using Mazesta.Desk;
namespace Mazesta.Desk.Tests;

public class DeskRulesTests
{
    [Theory]
    [InlineData("dfmrendering.com", "https://dfmrendering.com/")]
    [InlineData("https://www.dfmrendering.com/wp-json/mazesta/v1/", "https://www.dfmrendering.com/")]
    [InlineData("  https://x.ir:8443/a/b?c=d ", "https://x.ir:8443/")]
    [InlineData("http://127.0.0.1:8099", "http://127.0.0.1:8099/")]
    [InlineData("http://localhost", "http://localhost/")]
    public void Server_is_reduced_to_its_origin(string typed, string expected) => Assert.Equal(expected, DeskRules.Server(typed)?.ToString());

    [Theory]
    [InlineData("")] [InlineData(null)] [InlineData("http://evil.example")] [InlineData("ftp://x.ir")] [InlineData("https://user:pw@x.ir")] [InlineData("not a url")] [InlineData("javascript:alert(1)")]
    public void Server_refuses_what_could_leak_a_password(string? typed) => Assert.Null(DeskRules.Server(typed));

    [Theory]
    [InlineData("staff/login")] [InlineData("staff/jobs")] [InlineData("staff/jobs/12")] [InlineData("staff/label/job/12")] [InlineData("staff/jobs?q=R-12&page=2")] [InlineData("staff/find?q=%D8%B1%D8%B6%D8%A7")] [InlineData("staff/reports/rep-1a2b3c4d5e6f")]
    public void Staff_routes_are_allowed(string path) => Assert.True(DeskRules.PathAllowed(path));

    [Theory]
    [InlineData("")] [InlineData(null)] [InlineData("portal/me")] [InlineData("../wp/v2/users")] [InlineData("staff/../portal/me")] [InlineData("/staff/jobs")] [InlineData("staff//jobs")] [InlineData("staff/jobs?q=a;b")] [InlineData("staff/jobs?q=<x>")]
    [InlineData("https://evil.example/staff/jobs")] [InlineData("staff/jobs#x")] [InlineData("staff\\jobs")] [InlineData("staff/")] [InlineData("reports")]
    public void Anything_else_is_refused(string? path) => Assert.False(DeskRules.PathAllowed(path));

    [Fact] public void Only_get_and_post() { Assert.True(DeskRules.MethodAllowed("GET")); Assert.True(DeskRules.MethodAllowed("POST")); Assert.False(DeskRules.MethodAllowed("DELETE")); Assert.False(DeskRules.MethodAllowed(null)); }
}
