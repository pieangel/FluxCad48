from dataclasses import dataclass
from typing import Any, Callable, Dict


@dataclass(frozen=True)
class CadCommand:
    name: str
    handler: Callable[..., Any]
    description: str = ""


class CadAutomationPlugin:
    def __init__(self) -> None:
        self._commands: Dict[str, CadCommand] = {}

    def register_command(self, command: CadCommand) -> None:
        if command.name in self._commands:
            raise ValueError(f"Command '{command.name}' is already registered")
        self._commands[command.name] = command

    def run_command(self, name: str, *args: Any, **kwargs: Any) -> Any:
        command = self._commands.get(name)
        if command is None:
            raise KeyError(f"Unknown CAD command: {name}")
        return command.handler(*args, **kwargs)

    def list_commands(self) -> Dict[str, str]:
        return {name: command.description for name, command in self._commands.items()}
