using System.Text.Json;
using System.Text.RegularExpressions;

namespace TechRiders.Tests;

public class AspireDependencyTests
{
    // dotnet/aspire#14557 added IOException to the kubeconfig read retry. Older AppHosts can crash on startup with
    // "The process cannot access the file '...\aspire-dcp*\kubeconfig' because it is being used by another process."
    private static readonly Version FirstVersionWithKubeconfigSharingFix = new(13, 3, 0);

    private static string AppHostDirectory()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "TechRiders.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        return Path.Combine(root.FullName, "src", "TechRiders.AppHost");
    }

    [Fact]
    public void AppHostResolvesAspireHostingWithKubeconfigSharingViolationRetry()
    {
        var assets = Path.Combine(AppHostDirectory(), "obj", "project.assets.json");
        Assert.True(File.Exists(assets), $"Restore TechRiders.AppHost first: {assets}");
        using var document = JsonDocument.Parse(File.ReadAllText(assets));
        var resolved = document.RootElement.GetProperty("libraries").EnumerateObject()
            .Select(library => library.Name.Split('/'))
            .Where(parts => parts[0].Equals("Aspire.Hosting", StringComparison.OrdinalIgnoreCase))
            .Select(parts => Version.Parse(parts[1].Split('-')[0]))
            .ToList();
        var version = Assert.Single(resolved);
        Assert.True(version >= FirstVersionWithKubeconfigSharingFix,
            $"Aspire.Hosting {version} lacks the kubeconfig IOException retry (dotnet/aspire#14557); use >= {FirstVersionWithKubeconfigSharingFix}.");
    }

    [Fact]
    public void AppHostSdkAndHostingPackageVersionsMatch()
    {
        var project = File.ReadAllText(Path.Combine(AppHostDirectory(), "TechRiders.AppHost.csproj"));
        var sdk = Regex.Match(project, "<Sdk Name=\"Aspire.AppHost.Sdk\" Version=\"([^\"]+)\"").Groups[1].Value;
        var hosting = Regex.Match(project, "<PackageReference Include=\"Aspire.Hosting.AppHost\" Version=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(sdk);
        Assert.Equal(sdk, hosting);
        Assert.True(Version.Parse(sdk) >= FirstVersionWithKubeconfigSharingFix);
    }
}
