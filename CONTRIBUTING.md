# Cómo contribuir

¡Gracias por querer mejorar Carvis! Esta guía explica cómo proponer cambios para que se puedan revisar y aceptar rápido.

## Antes de empezar

- **¿Un fallo?** Abre un issue con la plantilla *Informe de fallo*. Si Carvis se cerró solo, al volver a abrirlo te ofrece copiar el informe, y eso ayuda mucho.
- **¿Una idea?** Abre un issue con la plantilla *Propuesta de mejora* antes de programarla, para hablarlo primero.
- **¿Un problema de seguridad?** No abras un issue público: sigue [SECURITY.md](SECURITY.md).

## Preparar el entorno

1. Instala el [SDK de .NET 8](https://dotnet.microsoft.com/download/dotnet/8.0) y [Ollama](https://ollama.com) con `qwen3:8b` y `nomic-embed-text`.
2. Clona y ejecuta:

   ```powershell
   git clone https://github.com/carvima07eng-dot/Carvis.git
   cd Carvis
   dotnet run --project src/Carvis.App
   ```

La interfaz y el Core también compilan y pasan los tests en Linux. Lo específico de Windows (ventanas, audio, OCR, Mica) está en `src/Carvis.Windows` y en Linux se sustituye por versiones vacías.

## Antes de abrir un pull request

Ejecuta lo mismo que la CI:

```powershell
dotnet build -c Release -warnaserror
dotnet test -c Release
dotnet format --verify-no-changes
```

Si tocas cómo el modelo elige herramientas (descripciones, palabras clave, prompt), pasa también las evaluaciones con el modelo real y pon el resultado en el PR:

```powershell
dotnet run --project tests/Carvis.Evals -- --report informe.md
```

## Estilo

- **Commits** con [Conventional Commits](https://www.conventionalcommits.org/es/): `feat(voz): …`, `fix(ui): …`, `docs: …`. Mejor commits pequeños que uno enorme.
- **Código** en inglés (nombres y comentarios) e **interfaz en español**. Comentarios solo donde haga falta explicar por qué.
- **Textos de la interfaz**:
  - Español natural y corto, de tú, sin jerga técnica.
  - Un error dice qué ha pasado y qué hacer: «No puedo hablar con Ollama. Ábrelo desde el menú Inicio y vuelve a intentarlo.», no «HttpRequestException».

## Sistema de diseño

Todo lo visual sale de dos archivos:

- `src/Carvis.App/Styles/DesignSystem.axaml`. Lleva los colores por tema (oscuro, claro y alto contraste) y el espaciado en múltiplos de 4 (`Space.4` … `Space.32`, `Inset.*`, `Gap.*`). También los radios (`Radius.4/8/12`), las sombras y las 5 tallas de letra (`Type.Caption`, `Body`, `BodyLarge`, `Subtitle`, `Title`).
- `src/Carvis.App/Styles/Icons.axaml`: solo iconos [Fluent UI System Icons](https://github.com/microsoft/fluentui-system-icons) de 20 px *regular*.

En las vistas no puede haber colores, márgenes ni tamaños de letra a mano: `DesignSystemTests` falla si los hay. Si te falta un valor, añade un token. Las animaciones duran entre 150 y 250 ms y se desactivan si Windows tiene las animaciones apagadas.

## Añadir una herramienta

Mira [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md). En resumen:

1. Implementa `ITool` en `src/Carvis.Core/Tools`.
2. Elige bien el riesgo (`Read`, `Low`, `Modify` o `Dangerous`). Si cambia algo, que se pueda deshacer.
3. Añade palabras clave en `ToolKeywords`, un icono en `ToolIcons` y casos en `tests/Carvis.Evals/cases.json`.
4. Escribe tests.

Al contribuir aceptas que tu código se publique con la [licencia MIT](LICENSE) del proyecto.
