using System.Xml.Linq;
using NUnit.Framework;

namespace HomeVault.Tests;

[TestFixture]
public sealed class ArchitectureTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [TestCase("src/HomeVault.Domain/HomeVault.Domain.csproj", new string[] { })]
    [TestCase("src/HomeVault.Application/HomeVault.Application.csproj", new[] { "HomeVault.Domain" })]
    [TestCase("src/HomeVault.Infrastructure/HomeVault.Infrastructure.csproj", new[] { "HomeVault.Application", "HomeVault.Domain" })]
    [TestCase("src/HomeVault.Playground/HomeVault.Playground.csproj", new[] { "HomeVault.Application", "HomeVault.Infrastructure", "HomeVault.Domain" })]
    [TestCase("tests/HomeVault.Tests/HomeVault.Tests.csproj", new[] { "HomeVault.Domain", "HomeVault.Application" })]
    [TestCase("tests/HomeVault.Infrastructure.Tests/HomeVault.Infrastructure.Tests.csproj", new[] { "HomeVault.Domain", "HomeVault.Application", "HomeVault.Infrastructure" })]
    public void ProjectReferencesStayWithinApprovedBoundaries(string projectPath, string[] allowedDependencies)
    {
        var fullPath = Path.Combine(RepositoryRoot, projectPath);
        var references = XDocument.Load(fullPath).Descendants("ProjectReference")
            .Select(reference => reference.Attribute("Include")!.Value).ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(references.Select(Path.GetFileNameWithoutExtension),
                Is.SubsetOf(allowedDependencies), $"Forbidden dependency in {projectPath}");

            foreach (var reference in references)
            {
                var resolvedPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(fullPath)!, reference));
                Assert.That(File.Exists(resolvedPath), Is.True, $"Missing project: {reference}");
                Assert.That(resolvedPath, Does.StartWith(RepositoryRoot + Path.DirectorySeparatorChar),
                    "Project references must stay inside this repository.");
            }
        }
    }

    [Test]
    public void DomainAllowsOnlyTheApprovedGuardPackage()
    {
        var domain = XDocument.Load(Path.Combine(RepositoryRoot, "src/HomeVault.Domain/HomeVault.Domain.csproj"));
        var sharedSettings = XDocument.Load(Path.Combine(RepositoryRoot, "Directory.Build.props"));

        Assert.That(domain.Descendants("PackageReference").Select(package => package.Attribute("Include")?.Value),
            Is.EquivalentTo(new[] { "Ardalis.GuardClauses" }), "Only the explicitly approved guard package is allowed.");
        Assert.That(sharedSettings.Descendants("PackageReference"), Is.Empty,
            "Shared settings must not introduce implicit Domain dependencies.");

        foreach (var document in new[] { domain, sharedSettings })
        {
            Assert.That(document.Descendants().Where(element =>
                element.Name.LocalName is "ProjectReference" or "Reference" or "FrameworkReference"),
                Is.Empty, "Domain must remain independent of project, external assembly, and framework references.");
        }
    }

    [Test]
    public void ApplicationHasNoInfrastructureOrWebPackageDependencies()
    {
        var application = XDocument.Load(Path.Combine(RepositoryRoot, "src/HomeVault.Application/HomeVault.Application.csproj"));
        Assert.That(application.Descendants().Where(element =>
            element.Name.LocalName is "PackageReference" or "FrameworkReference" or "Reference"), Is.Empty);
    }

    [Test]
    public void SolutionIncludesEverySourceAndTestProject()
    {
        var solution = XDocument.Load(Path.Combine(RepositoryRoot, "HomeVault.slnx"));
        var includedProjects = solution.Descendants("Project")
            .Select(project => Path.GetFullPath(Path.Combine(RepositoryRoot, project.Attribute("Path")!.Value)));
        var projectsOnDisk = new[] { "src", "tests" }
            .SelectMany(folder => Directory.EnumerateFiles(Path.Combine(RepositoryRoot, folder),
                "*.csproj", SearchOption.AllDirectories));

        Assert.That(includedProjects, Is.EquivalentTo(projectsOnDisk),
            "Projects omitted from the solution would escape the CI build.");
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
             directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "HomeVault.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Run architecture tests from a HomeVault source checkout.");
    }
}
