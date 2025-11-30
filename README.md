# SpaceLeft

`spaceleft` is a lightweight, command-line disk usage analyzer for Windows.

## Features
- **Drive Selection**: View all drives and their last scan time.
- **Persistence**: Saves scan data to `[Drive]_scan.gz` for faster loading.
- **Tabbed View**: Switch between **Files** and **Directories** lists (`TAB`).
- **Advanced Sorting**: Sort by Size (`S`), Name (`N`), or Path (`P`).
- **Long Path Support**: Uses Win32 APIs to handle paths beyond Windows' 260-character limit.
- **Actions**:
    - `Enter`: Open in **Explorer**.
    - `Shift + Enter`: Open in **PowerShell**.
    - `Ctrl + Enter`: Open in **Command Prompt**.

## How to Build
The tool is a single C# file that can be compiled with the standard .NET Framework compiler (`csc.exe`) usually natively available on Windows systems.

1.  Navigate to the project directory:
    
2.  Run the build script:
    ```cmd
    build.bat
    ```
    This will generate `spaceleft.exe` using `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`

## How to Run
Run the executable from the command line:
```cmd
.\spaceleft.exe
```

### Workflow
1.  **Select Drive**: Use Up/Down arrows to select a drive. Press `Shift+Enter` to scan a drive, or `Enter` to load scan data if already scanned.
2.  **View Results**:
    - Use `TAB` to switch between File list and Directory list.
    - Use `S`, `N`, `P` to sort.
    - Use Up/Down to navigate.
3.  **Take Action**:
    - Press `Enter` to open the selected item in Explorer.
    - Press `Shift+Enter` to open the selected item PowerShell.
    - Press `Ctrl+Enter` to open the selected item cmd.exe.
    - Press `Esc` to go back to Drive Selection.

## Technical Details

### Long Path Support
The tool uses Windows' native `FindFirstFile` and `FindNextFile` APIs via P/Invoke, which support paths up to 32,767 characters when using the `\\?\` prefix. This ensures:
- **Complete disk coverage**: No files or directories are skipped due to path length
- **Accurate reporting**: All disk space is accounted for
- **Native performance**: Direct Win32 API calls are faster than .NET wrappers
