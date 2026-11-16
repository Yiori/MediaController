# MediaController

Controle a mídia do seu celular Android (play/pause, faixas, volume) pelo Windows, usando **atalhos globais personalizáveis**. A conexão é por **USB**, via ADB.

## Requisitos
- Windows 10/11 com .NET 10 Desktop Runtime
- No celular: **Opções do desenvolvedor → Depuração USB** ativada
- O `adb` é baixado automaticamente na primeira execução (fica em `%AppData%\MediaController\platform-tools`)

## Uso
```powershell
dotnet run
# ou gerar executável:
dotnet publish -c Release
```
1. Conecte o celular por USB e aceite "Permitir depuração USB".
2. O ícone na bandeja fica **verde** quando o celular está conectado.
3. Clique duas vezes no ícone para abrir as configurações e mudar os atalhos.

## Atalhos padrão
| Ação | Atalho |
|---|---|
| Play/Pause | Ctrl + Alt + Espaço |
| Próxima | Ctrl + Alt + → |
| Anterior | Ctrl + Alt + ← |
| Volume + / − | Ctrl + Alt + ↑ / ↓ |

As configurações ficam em `%AppData%\MediaController\settings.json`.
