import importlib.util
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("new_game", ROOT / "scripts" / "new_game.py")
generator = importlib.util.module_from_spec(spec)
spec.loader.exec_module(generator)


class GeneratorTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="CompanionGenerator-")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.feed = self.root / "feed & space"
        self.feed.mkdir()
        for package in ("Companion.Launcher.Core", "Companion.Launcher.Windows", "Companion.Launcher.Avalonia", "Companion.Launcher.Android"):
            (self.feed / f"{package}.0.2.0.nupkg").write_bytes(b"synthetic test marker")

    def test_reuse_and_isolation(self):
        a, b = self.root / "a", self.root / "b"
        generator.create("game-a", 'A "Quoted" Game', a, self.feed)
        generator.create("game-b", "Game B", b, self.feed)
        self.assertNotEqual((a / "GameAdapter.cs").read_text(), (b / "GameAdapter.cs").read_text())
        self.assertIn('Version="[0.2.0]"', (a / "Game.Companion.csproj").read_text())
        self.assertNotIn("ProjectReference", (a / "Game.Companion.csproj").read_text())
        self.assertIn("&amp;", (a / "nuget.config").read_text())
        self.assertIn("https://api.nuget.org/v3/index.json", (a / "nuget.config").read_text())
        self.assertIn('pattern="Companion.Launcher.*"', (a / "nuget.config").read_text())
        self.assertFalse((a / "src").exists())

    def test_portable_and_android_starters(self):
        for platform in ("desktop", "android"):
            output = self.root / platform
            generator.create("sample-" + platform, "Sample", output, self.feed, platform)
            project = (output / "Game.Companion.csproj").read_text()
            self.assertIn("Companion.Launcher.Avalonia", project)
            self.assertNotIn("UseWindowsForms", project)
            if platform == "android":
                self.assertIn("net10.0-android", project)
                self.assertTrue((output / "Properties/AndroidManifest.xml").is_file())
                self.assertIn("AvaloniaAndroidApplication", (output / "MainActivity.cs").read_text())
                self.assertIn("Theme.AppCompat.NoActionBar", (output / "Resources/values/styles.xml").read_text())

    def test_never_overwrites(self):
        output = self.root / "existing"
        output.mkdir()
        marker = output / "owner.txt"
        marker.write_text("keep")
        with self.assertRaises(ValueError):
            generator.create("valid", "Valid", output, self.feed)
        self.assertEqual(marker.read_text(), "keep")
        self.assertEqual(len(list(output.iterdir())), 1)

    def test_rejects_unsafe_identity_before_writing(self):
        for value in ("..", "../outside", "con", "con.txt", "lpt1", "bad.", "UPPER", "bad name"):
            with self.subTest(value=value), self.assertRaises(ValueError):
                generator.create(value, "Title", self.root / "not-created", self.feed)
            self.assertFalse((self.root / "not-created").exists())

    def test_requires_both_pinned_packages(self):
        (self.feed / "Companion.Launcher.Core.0.2.0.nupkg").unlink()
        with self.assertRaises(ValueError):
            generator.create("valid", "Title", self.root / "not-created", self.feed)
        self.assertFalse((self.root / "not-created").exists())


if __name__ == "__main__":
    unittest.main()
