using Spar.Tool.Frontend;
using Spar.Tool.Init;

namespace Spar.Tool.Tests;

/// <summary>init's text edits: Vite's configuration, package.json, the project file, and the diff it shows.</summary>
public sealed class FrontendEditTests
{
    [Fact]
    public void The_plugin_goes_after_the_last_import_and_last_in_plugins_in_the_files_own_style()
    {
        const string scaffold = "import react from '@vitejs/plugin-react'\nimport { defineConfig } from 'vite'\n\n// https://vite.dev/config/\nexport default defineConfig({\n  plugins: [react()],\n})\n";

        var (text, problem) = ViteConfig.AddPlugin(scaffold, "vite.config.ts");

        Assert.Null(problem);
        Assert.Equal("import react from '@vitejs/plugin-react'\nimport { defineConfig } from 'vite'\nimport spar from '@spar/app/vite'\n\n// https://vite.dev/config/\nexport default defineConfig({\n  plugins: [react(), spar()],\n})\n", text);
        Assert.Equal((true, true), ViteConfig.Inspect(text!));
    }

    [Fact]
    public void A_multi_line_array_gets_its_own_line_with_semicolons_double_quotes_and_CRLF_kept()
    {
        const string config = "import vue from \"@vitejs/plugin-vue\";\r\nimport { defineConfig } from \"vite\";\r\n\r\nexport default defineConfig({\r\n  plugins: [\r\n    vue()\r\n  ],\r\n});\r\n";

        var (text, _) = ViteConfig.AddPlugin(config, "vite.config.mts");

        Assert.Equal("import vue from \"@vitejs/plugin-vue\";\r\nimport { defineConfig } from \"vite\";\r\nimport spar from \"@spar/app/vite\";\r\n\r\nexport default defineConfig({\r\n  plugins: [\r\n    vue(),\r\n    spar(),\r\n  ],\r\n});\r\n", text);
    }

    [Theory]
    [InlineData("plugins: []", "plugins: [spar()]")]
    [InlineData("plugins: [react(),]", "plugins: [react(), spar()]")]
    public void Arrays_of_every_plain_shape_get_the_call(string before, string after)
    {
        var config = $"import {{ defineConfig }} from 'vite'\nexport default defineConfig({{ {before} }})\n";

        var (text, _) = ViteConfig.AddPlugin(config, "vite.config.js");

        Assert.Contains(after, text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_configuration_already_wired_is_left_as_it_is_and_one_with_spar_taken_uses_another_name()
    {
        const string wired = "import spar from \"@spar/app/vite\";\nexport default { plugins: [spar()] };\n";
        const string taken = "import { defineConfig } from 'vite'\nconst spar = 1\nexport default defineConfig({ plugins: [] })\n";

        Assert.Equal(wired, ViteConfig.AddPlugin(wired, "vite.config.js").Text);
        var (renamed, _) = ViteConfig.AddPlugin(taken, "vite.config.js");
        Assert.Contains("import sparPlugin from '@spar/app/vite'", renamed, StringComparison.Ordinal);
        Assert.Contains("plugins: [sparPlugin()]", renamed, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("export default { build: {} }", "no plugins")]
    [InlineData("const plugins = [a()]; export default { plugins }", "no plugins")]
    [InlineData("const a = { plugins: [x()] }; export default { plugins: [y()] }", "2 plugins arrays")]
    [InlineData("export default { plugins: [react({ babel: { plugins: ['x'] } })] }", "2 plugins arrays")]
    [InlineData("import spar from '@spar/app/vite'\nexport default { plugins: [] }", "does not call it")]
    public void A_configuration_too_unusual_to_edit_is_left_alone_with_the_reason(string config, string reason)
    {
        var (text, problem) = ViteConfig.AddPlugin(config, "vite.config.ts");

        Assert.Null(text);
        Assert.Contains(reason, problem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_CommonJS_configuration_is_never_edited_and_comments_and_strings_never_count()
    {
        Assert.NotNull(ViteConfig.AddPlugin("module.exports = { plugins: [] }", "vite.config.cjs").Problem);

        const string commented = "// plugins: [old()]\nconst s = \"plugins: [x()]\"\nexport default { plugins: [] }\n// import spar from '@spar/app/vite'\n";
        Assert.Equal((false, false), ViteConfig.Inspect(commented));
        Assert.Contains("plugins: [spar()]", ViteConfig.AddPlugin(commented, "vite.config.js").Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Package_json_gets_the_dependency_first_in_its_object_with_the_files_indentation()
    {
        const string before = "{\n    \"name\": \"web\",\n    \"dependencies\": {\n        \"react\": \"^19.3.1\"\n    }\n}\n";

        var after = PackageJson.Set(before, "dependencies", "@spar/app", "file:../obj/spar/client", first: true);

        Assert.Equal("{\n    \"name\": \"web\",\n    \"dependencies\": {\n        \"@spar/app\": \"file:../obj/spar/client\",\n        \"react\": \"^19.3.1\"\n    }\n}\n", after);
    }

    [Fact]
    public void Package_json_gets_a_missing_object_a_replaced_value_and_a_script_at_the_end()
    {
        const string before = "{\r\n  \"name\": \"web\",\r\n  \"scripts\": { \"build\": \"vite build\" }\r\n}\r\n";

        var withDependencies = PackageJson.Set(before, "dependencies", "@spar/app", "file:x", first: true);
        var replaced = PackageJson.Set(withDependencies, "dependencies", "@spar/app", "file:../obj/spar/client", first: true);
        var withDev = PackageJson.Set(replaced, "scripts", "dev", "vite", first: false);

        Assert.Equal("{\r\n  \"name\": \"web\",\r\n  \"scripts\": { \"build\": \"vite build\",\r\n    \"dev\": \"vite\" },\r\n  \"dependencies\": {\r\n    \"@spar/app\": \"file:../obj/spar/client\"\r\n  }\r\n}\r\n", withDev);
        Assert.Equal("file:../obj/spar/client", PackageJson.Read(withDev).Contents!.SparDependency);
    }

    [Fact]
    public void The_client_specifier_is_relative_with_slashes_and_names_the_client_from_any_folder()
    {
        var app = Path.GetFullPath(Path.Combine("x", "App"));
        var client = Path.Combine(app, "obj", "spar", "client");

        Assert.Equal("file:../obj/spar/client", PackageJson.ClientSpecifier(Path.Combine(app, "frontend"), client));
        Assert.Equal("file:../../obj/spar/client", PackageJson.ClientSpecifier(Path.Combine(app, "web", "page"), client));
        Assert.True(PackageJson.NamesClient("file:../obj/spar/client/", Path.Combine(app, "frontend"), client));
        Assert.False(PackageJson.NamesClient("^1.0.0", Path.Combine(app, "frontend"), client));
    }

    [Fact]
    public void SparFrontendPath_is_added_at_the_end_of_the_first_property_group_or_replaced()
    {
        var template = File.ReadAllText(Path.Combine(TemplateApp.Source, "MyApp.csproj"));

        var added = ProjectFileEdit.SetFrontendPath(template, "ui")!;
        var replaced = ProjectFileEdit.SetFrontendPath(added, "web/page")!;

        Assert.Contains("<SparPlatforms>windows;linux</SparPlatforms>\n\n    <!-- The page's folder (spar init frontend); frontend/ when it is not set. -->\n    <SparFrontendPath>ui</SparFrontendPath>\n  </PropertyGroup>", added, StringComparison.Ordinal);
        Assert.Contains("<SparFrontendPath>web/page</SparFrontendPath>", replaced, StringComparison.Ordinal);
        Assert.DoesNotContain("<SparFrontendPath>ui", replaced, StringComparison.Ordinal);
    }

    [Fact]
    public void The_diff_shows_each_change_with_three_lines_of_context()
    {
        var before = string.Join('\n', Enumerable.Range(1, 12).Select(i => $"line {i}")) + "\n";
        var after = before.Replace("line 6\n", "line 6\nadded\n", StringComparison.Ordinal).Replace("line 12\n", "twelve\n", StringComparison.Ordinal);

        var diff = LineDiff.Unified("frontend/a.txt", before, after);

        Assert.Equal("--- a/frontend/a.txt\n+++ b/frontend/a.txt\n@@ -4,9 +4,10 @@\n line 4\n line 5\n line 6\n+added\n line 7\n line 8\n line 9\n line 10\n line 11\n-line 12\n+twelve\n", diff);
    }
}
