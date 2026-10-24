import json
import tempfile
import unittest
from pathlib import Path

import check_versions


def make_repo(props="6.4.9", manifest="6.4.9", installer="6.4.9", csproj="<Project><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>"):
    root = Path(tempfile.mkdtemp())
    (root / "host" / "installer").mkdir(parents=True)
    (root / "host" / "App").mkdir()
    (root / "extension").mkdir()
    if props is not None:
        (root / "host" / "Directory.Build.props").write_text("<Project><PropertyGroup><Version>%s</Version></PropertyGroup></Project>" % props)
    if manifest is not None:
        # CRLF and a BOM, as the real manifest has / may have
        (root / "extension" / "manifest.json").write_bytes(("\ufeff" + json.dumps({"manifest_version": 3, "version": manifest}, indent=2).replace("\n", "\r\n")).encode("utf-8"))
    if installer is not None:
        (root / "host" / "installer" / "setup.iss").write_text('[Setup]\r\n#define MyAppVersion "%s"\r\nAppVersion={#MyAppVersion}\r\n' % installer)
    (root / "host" / "App" / "App.csproj").write_text(csproj)
    return root


class CheckVersionsTests(unittest.TestCase):
    def test_matching_versions_pass(self):
        versions, problems = check_versions.check(make_repo())
        self.assertEqual([], problems)
        self.assertEqual({"6.4.9"}, set(versions.values()))

    def test_a_different_extension_version_fails(self):
        _, problems = check_versions.check(make_repo(manifest="6.4.8"))
        self.assertTrue(any("differ" in p for p in problems), problems)

    def test_a_different_installer_version_fails(self):
        _, problems = check_versions.check(make_repo(installer="6.0.0"))
        self.assertTrue(any("differ" in p for p in problems), problems)

    def test_a_missing_file_is_a_clear_error(self):
        _, problems = check_versions.check(make_repo(installer=None))
        self.assertTrue(any("setup.iss" in p and "could not be read" in p for p in problems), problems)

    def test_a_missing_define_is_a_clear_error(self):
        root = make_repo()
        (root / "host" / "installer" / "setup.iss").write_text("[Setup]\r\nAppVersion=1.0.0\r\n")
        _, problems = check_versions.check(root)
        self.assertTrue(any("no version found" in p for p in problems), problems)

    def test_a_malformed_version_fails(self):
        _, problems = check_versions.check(make_repo(props="6.4", manifest="6.4", installer="6.4"))
        self.assertTrue(any("MAJOR.MINOR.PATCH" in p for p in problems), problems)

    def test_a_project_that_sets_its_own_version_fails(self):
        _, problems = check_versions.check(make_repo(csproj="<Project><PropertyGroup><Version>1.0.0</Version></PropertyGroup></Project>"))
        self.assertTrue(any("sets <Version>" in p for p in problems), problems)

    def test_package_reference_versions_are_not_overrides(self):
        csproj = '<Project><ItemGroup><PackageReference Include="X" Version="1.2.3" /></ItemGroup></Project>'
        _, problems = check_versions.check(make_repo(csproj=csproj))
        self.assertEqual([], problems)

    def test_main_returns_the_exit_code(self):
        self.assertEqual(0, check_versions.main(["x", str(make_repo())]))
        self.assertEqual(1, check_versions.main(["x", str(make_repo(manifest="1.0.0"))]))


if __name__ == "__main__":
    unittest.main()
