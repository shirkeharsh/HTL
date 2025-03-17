# HostLocal - Local Network Cloud Storage

HostLocal is a C#-based application that enables users to upload, access, and manage files over the same LAN (Local Area Network). It functions as a private cloud storage system accessible through a web browser.

## Features
- **File Upload & Management**: Users can upload, view, and delete files.
- **LAN Access**: Access the stored files from any device on the same network.
- **User Authentication**: Secure login system for managing access.
- **Auto File Categorization**: Automatically sorts files into folders (e.g., images, videos, documents).
- **Download & Preview**: Supports in-browser previews and downloads.
- **Minimal Setup**: No external hosting required, runs on localhost.

## Technologies Used
- **Language**: C# (.NET 8.0)
- **Frontend**: HTML, CSS, JavaScript
- **Backend**: ASP.NET Core
- **Database**: SQLite / MySQL (optional for user authentication)
- **Networking**: Localhost server accessible via LAN

## Installation Guide
### Prerequisites
- Windows OS (10 or later)
- .NET 8.0 SDK installed
- Visual Studio or VS Code

### Steps to Install
1. Clone the repository:
   ```sh
   git clone https://github.com/yourusername/hostlocal.git
   ```
2. Open the project in Visual Studio or VS Code.
3. Restore dependencies:
   ```sh
   dotnet restore
   ```
4. Build and run the application:
   ```sh
   dotnet run
   ```
5. Find your local IP address:
   - Open Command Prompt and run:
     ```sh
     ipconfig
     ```
   - Look for **IPv4 Address** (e.g., `192.168.1.100`).
6. Access the app in a browser:
   ```
   http://192.168.1.100:5000/
   ```

## Usage
1. **Upload Files**: Drag and drop or select files manually.
2. **Manage Files**: View, delete, and download stored files.
3. **Secure Access**: Log in to manage uploads (if authentication is enabled).
4. **Access from Any Device**: Open the URL on any device connected to the same LAN.

## Screenshots
![Upload Page](screenshots/upload.png)
![File Management](screenshots/files.png)

## Future Enhancements
- Remote access
