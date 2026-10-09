# Simple-PDF

A free, lightweight Windows PDF reader for researchers who want to open a paper, read it, and print selected pages.

![Simple-PDF welcome screen](docs/welcome-en.png)

## Why Simple-PDF?

- **Free and open source.** No subscription, account, or paid reading features. The application source is available under the MIT license.
- **Small storage footprint.** The portable application is approximately 0.94 MiB, with a ZIP download of approximately 0.29 MiB. It uses the shared Microsoft Edge WebView2 Runtime rather than bundling an entire browser. These figures exclude that runtime, the .NET Framework, and the runtime's growing profile/cache data.
- **Focused on research reading.** Reading downloaded papers usually needs navigation, search, zoom, and printing. Simple-PDF leaves out editing, OCR, conversion, electronic signatures, cloud services, and other full-suite workflows that many researchers do not need for this task. It offers a compact reading interface instead of the broader toolset of products such as Adobe Acrobat.
- **Local documents.** Simple-PDF reads files from your computer and does not upload them or require a cloud account. The WebView2 Runtime is maintained by Microsoft and may perform its own updates or background services.
- **English by default, with a Chinese option.** Change the language in the toolbar. The choice is remembered, and an open document is reopened when the renderer changes language.
- **Portable.** Extract the ZIP and run `Simple-PDF.exe`. No application installer is needed when the prerequisites are already installed.

## Download and start

1. Download `Simple-PDF-Windows-x64.zip` from [Releases](https://github.com/qxyumr-dot/Simple-PDF/releases).
2. Extract the entire folder. Keep the executable, DLLs, and configuration file together.
3. Run `Simple-PDF.exe`.
4. Select **Open PDF**. The file picker starts in your Downloads folder.

You can also drop a PDF onto the executable icon or pass its path on the command line:

```powershell
.\Simple-PDF.exe "C:\Users\YourName\Downloads\paper.pdf"
```

## Read and print

Use the document toolbar for page numbers, zoom, fit-to-page, rotation, and search. Select **Print** to open print preview, choose a printer and page range, and confirm the job.

| Shortcut | Action |
| --- | --- |
| Ctrl+O | Open a local PDF |
| Ctrl+P | Open print preview |
| Ctrl+F | Search the document |

Use the language selector to choose **English** or **Chinese**. A fresh installation defaults to English; later launches remember your selection. Windows-owned file and printer dialogs can still follow the Windows display language.

## Requirements

- 64-bit Windows 10 or Windows 11.
- .NET Framework 4.6.2 or later.
- [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/).

PDF editing, annotations, OCR, document conversion, and cloud synchronization are outside the scope of this reader. It uses WebView2's built-in PDF renderer; PDF-specific limitations and print restrictions can depend on the document and runtime.

## Build from source

```powershell
git clone https://github.com/qxyumr-dot/Simple-PDF.git
cd Simple-PDF
powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1
```

The build script downloads the pinned WebView2 SDK from NuGet, compiles the application using the .NET Framework compiler, and creates `dist\Simple-PDF-Windows-x64.zip`. Internet access is needed for the first SDK restore. `Simple-PDF.csproj` is also provided for Visual Studio/MSBuild after SDK restoration.

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\test.ps1
```

The smoke tests use a generated three-page PDF. They check English and Chinese screens, switching languages with an open document, filenames containing spaces and special characters, rejection of invalid/missing files, PDF rendering, and the print-preview API. They do not send a physical print job.

## Project layout

```text
src/                 Application code, configuration, and translation resources
assets/              Application icon
scripts/             Build and smoke-test scripts
docs/                Screenshots and third-party license
.github/workflows/   Windows build and release automation
```

Application settings and WebView2 profiles are stored under `%LOCALAPPDATA%\Simple-PDF`. Remove that folder while the application is closed to reset language preferences and cached runtime data.

## License

The application is licensed under [MIT](LICENSE). The WebView2 SDK redistribution terms are included in [docs/WebView2-LICENSE.txt](docs/WebView2-LICENSE.txt); the WebView2 Runtime remains subject to Microsoft's terms.
