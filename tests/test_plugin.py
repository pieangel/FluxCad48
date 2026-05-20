import unittest

from fluxcad48_plugin import CadAutomationPlugin, CadCommand


class CadAutomationPluginTests(unittest.TestCase):
    def test_register_and_run_command(self) -> None:
        plugin = CadAutomationPlugin()
        plugin.register_command(CadCommand(name="sum", handler=lambda a, b: a + b, description="Adds values"))

        self.assertEqual(plugin.run_command("sum", 2, 3), 5)

    def test_register_duplicate_command_raises(self) -> None:
        plugin = CadAutomationPlugin()
        command = CadCommand(name="noop", handler=lambda: None)
        plugin.register_command(command)

        with self.assertRaises(ValueError):
            plugin.register_command(command)

    def test_run_unknown_command_raises(self) -> None:
        plugin = CadAutomationPlugin()

        with self.assertRaises(KeyError):
            plugin.run_command("missing")

    def test_list_commands_includes_descriptions(self) -> None:
        plugin = CadAutomationPlugin()
        plugin.register_command(CadCommand(name="draft", handler=lambda: "ok", description="Create draft"))

        self.assertEqual(plugin.list_commands(), {"draft": "Create draft"})


if __name__ == "__main__":
    unittest.main()
