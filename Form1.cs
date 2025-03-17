using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using QRCoder;


namespace FileManagerServer
{
    public partial class Form1 : Form
    {
        private HttpListener listener;
        private string sharedFolder;

        public Form1()
        {
            InitializeComponent();
            this.Load += Form1_Load;
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            lblStatus.Text = "Server not started...";
            btnStopServer.Enabled = false; // Disable Stop button initially
        }

        private void btnBrowse_Click(object sender, EventArgs e)
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    txtFolderPath.Text = dialog.SelectedPath;
                }
            }
        }
        private void GenerateQRCode(string url)
        {
            using (QRCodeGenerator qrGenerator = new QRCodeGenerator())
            {
                using (QRCodeData qrCodeData = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q))
                {
                    using (QRCode qrCode = new QRCode(qrCodeData))
                    {
                        pictureBoxQR.Image = qrCode.GetGraphic(3); // Adjust size
                    }
                }
            }
        }


        private async void btnStartServer_Click(object sender, EventArgs e)
        {
            if (!FirewallRuleExists("FileManagerServer"))
            {
                DialogResult result = MessageBox.Show("This application needs to open port 8080 in the firewall. Allow it?",
                    "Firewall Permission", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

                if (result == DialogResult.Yes)
                {
                    AddFirewallRule();
                }
                else
                {
                    MessageBox.Show("The server might not be accessible from other devices without firewall permissions.",
                        "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }

            sharedFolder = txtFolderPath.Text;
            if (!Directory.Exists(sharedFolder))
            {
                MessageBox.Show("Folder does not exist!");
                return;
            }

            btnStartServer.Enabled = false;  // Disable Start button
            btnStopServer.Enabled = true;   // Enable Stop button

            await StartHttpServer();
        }

        private void btnStopServer_Click(object sender, EventArgs e)
        {
            StopHttpServer();
        }

        private void AddFirewallRule()
        {
            try
            {
                Process process = new Process();
                process.StartInfo.FileName = "netsh";
                process.StartInfo.Arguments = "advfirewall firewall add rule name=\"FileManagerServer\" dir=in action=allow protocol=TCP localport=8080";
                process.StartInfo.Verb = "runas";
                process.StartInfo.CreateNoWindow = true;
                process.StartInfo.UseShellExecute = true;
                process.Start();
                process.WaitForExit();
                MessageBox.Show("Firewall rule added successfully!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to add firewall rule: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private bool FirewallRuleExists(string ruleName)
        {
            try
            {
                Process process = new Process();
                process.StartInfo.FileName = "netsh";
                process.StartInfo.Arguments = "advfirewall firewall show rule name=\"" + ruleName + "\"";
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;
                process.Start();

                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                return output.Contains(ruleName);
            }
            catch
            {
                return false;
            }
        }

        private async Task StartHttpServer()
        {
            try
            {
                if (listener != null)
                {
                    Log("Server is already running.");
                    return;
                }

                listener = new HttpListener();
                string localIP = GetLocalIPv4();
                string url = $"http://{localIP}:8080/";
                listener.Prefixes.Add(url);
                listener.Start();

                Log($"Server started at {url}");
                this.Invoke((Action)(() =>
                {
                    lblStatus.Text = $"Paste in your browser\n{url} or\nScan the QR";
                    GenerateQRCode(url); // Generate QR code
                }));

                while (listener.IsListening)
                {
                    HttpListenerContext context;
                    try
                    {
                        context = await listener.GetContextAsync();
                    }
                    catch (ObjectDisposedException)
                    {
                        Log("Listener was disposed.");
                        break;
                    }

                    _ = Task.Run(() => ProcessRequest(context));
                }
            }
            catch (Exception ex)
            {
                Log($"Error: {ex.Message}");
                MessageBox.Show($"Error: {ex.Message}", "Server Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }


        private void StopHttpServer()
        {
            try
            {
                if (listener != null)
                {
                    if (listener.IsListening)
                    {
                        listener.Stop();
                    }
                    listener.Close();
                    listener = null; // Set to null to prevent reuse
                    Log("Server stopped.");

                    // Enable Start button and disable Stop button
                    this.Invoke((Action)(() =>
                    {
                        lblStatus.Text = "Server stopped.";
                        btnStartServer.Enabled = true;  // Enable Start button
                        btnStopServer.Enabled = false;  // Disable Stop button
                    }));
                }
            }
            catch (ObjectDisposedException)
            {
                Log("Server already disposed.");
            }
            catch (Exception ex)
            {
                Log($"Error stopping server: {ex.Message}");
            }
        }


        private void ProcessRequest(HttpListenerContext context)
        {
            string requestedUrl = context.Request.Url.AbsolutePath.Trim('/');
            requestedUrl = WebUtility.UrlDecode(requestedUrl); // Decode URL to handle spaces and special chars

            string requestedPath = Path.Combine(sharedFolder, requestedUrl);

            if (Directory.Exists(requestedPath))
            {
                ServeDirectory(context, requestedPath);
            }
            else if (File.Exists(requestedPath))
            {
                ServeFile(context, requestedPath);
            }
            else if (context.Request.HttpMethod == "POST" && context.Request.Url.AbsolutePath == "/upload")
            {
                HandleFileUpload(context);
            }

            else
            {
                SendResponse(context.Response, "404 Not Found", 404);
            }

        }

        private void HandleFileUpload(HttpListenerContext context)
        {
            try
            {
                if (!context.Request.HasEntityBody)
                {
                    SendResponse(context.Response, "No file uploaded!", 400);
                    return;
                }

                using (var stream = context.Request.InputStream)
                using (var reader = new BinaryReader(stream))
                {
                    var boundary = context.Request.ContentType.Split('=').Last();
                    var body = reader.ReadBytes((int)context.Request.ContentLength64);
                    var bodyStr = Encoding.UTF8.GetString(body);

                    // Extract filename
                    var match = System.Text.RegularExpressions.Regex.Match(bodyStr, @"filename=""(.+?)""");
                    if (!match.Success)
                    {
                        SendResponse(context.Response, "Invalid file upload", 400);
                        return;
                    }

                    string fileName = match.Groups[1].Value;
                    string filePath = Path.Combine(sharedFolder, fileName);

                    // Extract file data
                    int fileStart = bodyStr.IndexOf("\r\n\r\n") + 4;
                    byte[] fileData = body.Skip(fileStart).ToArray();

                    // Save the file
                    File.WriteAllBytes(filePath, fileData);
                }

                SendResponse(context.Response, "File uploaded successfully!", 200);
            }
            catch (Exception ex)
            {
                SendResponse(context.Response, $"Upload error: {ex.Message}", 500);
            }
        }



        private void ServeDirectory(HttpListenerContext context, string path)
        {
            string[] files = Directory.GetFiles(path);
            string[] dirs = Directory.GetDirectories(path);

            StringBuilder sb = new StringBuilder(@"
<html>
<head>
    <meta charset='UTF-8'>
    <title>File Manager</title>
    <style>
        body {
            font-family: Arial, sans-serif;
            background-color: #f4f4f4;
            margin: 20px;
        }
        .container {
            max-width: 800px;
            margin: 0 auto;
            background: white;
            padding: 20px;
            box-shadow: 0px 0px 10px rgba(0, 0, 0, 0.1);
            border-radius: 8px;
        }
        h2 {
            text-align: center;
            color: #333;
        }
        .breadcrumbs {
            margin-bottom: 10px;
        }
        a {
            text-decoration: none;
            color: #007bff;
        }
        a:hover {
            text-decoration: underline;
        }
        .grid {
            display: grid;
            grid-template-columns: repeat(auto-fill, minmax(150px, 1fr));
            gap: 15px;
            padding: 10px;
        }
        .item {
            text-align: center;
            padding: 10px;
            background: #fff;
            border-radius: 5px;
            box-shadow: 2px 2px 5px rgba(0, 0, 0, 0.1);
            transition: transform 0.2s;
        }
        .item:hover {
            transform: scale(1.05);
        }
        .item img {
            width: 50px;
            height: 50px;
            margin-bottom: 5px;
        }
    </style>
</head>
<script>
        function downloadSelected() {
            let checkboxes = document.querySelectorAll('input[name=\""fileCheckbox\""]:checked');
            checkboxes.forEach(checkbox => {
                window.open(checkbox.value, '_blank');
            });
        }
    </script>
<body>
<div class='container'>
    <h2>📂 File Manager</h2>");

            // Breadcrumbs
            string relativePath = path.Replace(sharedFolder, "").TrimStart(Path.DirectorySeparatorChar);
            sb.Append("<div class='breadcrumbs'><a href=\"/\">Home</a>");
            string[] parts = relativePath.Split(Path.DirectorySeparatorChar);
            string currentPath = "";
            foreach (var part in parts)
            {
                if (!string.IsNullOrEmpty(part))
                {
                    currentPath += "/" + part;
                    sb.Append($" / <a href=\"{Uri.EscapeDataString(currentPath)}\">{part}</a>");
                }
            }
            sb.Append("</div>");

            sb.Append("<div class='grid'>"); sb.Append(@"
<form action='/upload' method='post' enctype='multipart/form-data'>
    <input type='file' name='file' required>
    <input type='submit' value='Upload'>
</form><br>
");
            // Back button if not in root
            if (path != sharedFolder)
                sb.Append("<div class='item'><a href=\"../\"><img src='https://img.icons8.com/ios/50/000000/up.png'/><br>Back</a></div>");

            // Directories
            foreach (var dir in dirs)
            {
                string dirName = Path.GetFileName(dir);
                sb.Append($"<div class='item'><a href=\"{Uri.EscapeDataString(dirName)}/\"><img src='https://img.icons8.com/ios/50/000000/folder.png'/><br>{dirName}</a></div>");
            }

            // Files with preview for images
            foreach (var file in files)
            {
                string fileName = Path.GetFileName(file);
                string encodedFileName = WebUtility.HtmlEncode(fileName);
                string fileUrl = Uri.EscapeDataString(fileName);
                string extension = Path.GetExtension(fileName).ToLower();
                string iconUrl = GetFileIcon(fileName);

                if (extension == ".jpg" || extension == ".jpeg" || extension == ".png" || extension == ".gif")
                {
                    // Display image preview for supported formats
                    sb.Append($"<div class='item'><a href=\"{fileUrl}\"><img src=\"{fileUrl}\" style='width:100px; height:auto;'/><br>{encodedFileName}</a></div>");
                }
                else
                {
                    // Show standard file icon
                    sb.Append($"<div class='item'><a href=\"{fileUrl}\"><img src=\"{iconUrl}\"/><br>{encodedFileName}</a></div>");
                }
            }
            foreach (var file in files)
            {
                string fileName = Path.GetFileName(file);
                string fileUrl = Uri.EscapeDataString(fileName);
                string encodedFileName = WebUtility.HtmlEncode(fileName);
                string iconUrl = GetFileIcon(fileName);

                sb.Append($@"
        <div class='item'>
            <input type='checkbox' name='fileCheckbox' value='{fileUrl}'>
            <a href='{fileUrl}'><img src='{iconUrl}'/><br>{encodedFileName}</a>
        </div>");
            }

            sb.Append(@"
    </div>
    <button onclick='downloadSelected()' style='margin-top: 20px; padding: 10px 15px; font-size: 16px;'>Download Selected</button>
</div>
</body></html>");
            sb.Append("</div></body></html>");
            SendResponse(context.Response, sb.ToString(), 200);
        }


        private string GetFileIcon(string fileName)
        {
            string extension = Path.GetExtension(fileName).ToLower();

            var iconMap = new Dictionary<string, string>
    {
        { ".jpg", "https://img.icons8.com/ios/50/000000/image-file.png" },
        { ".png", "https://img.icons8.com/ios/50/000000/image-file.png" },
        { ".txt", "https://img.icons8.com/ios/50/000000/document.png" },
        { ".pdf", "https://img.icons8.com/ios/50/000000/pdf.png" },
        { ".mp3", "https://img.icons8.com/ios/50/000000/music.png" },
        { ".mp4", "https://img.icons8.com/ios/50/000000/video.png" },
        { ".zip", "https://img.icons8.com/ios/50/000000/zip.png" },
        { ".doc", "https://img.icons8.com/ios/50/000000/word.png" },
        { ".docx", "https://img.icons8.com/ios/50/000000/word.png" },
        { ".xlsx", "https://img.icons8.com/ios/50/000000/excel.png" },
        { ".exe", "https://img.icons8.com/ios/50/000000/application.png" },
        { ".html", "https://img.icons8.com/ios/50/000000/code.png" }
    };

            return iconMap.ContainsKey(extension) ? iconMap[extension] : "https://img.icons8.com/ios/50/000000/file.png";
        }


        private void ServeFile(HttpListenerContext context, string filePath)
        {
            try
            {
                string extension = Path.GetExtension(filePath).ToLower();
                string mimeType = GetMimeType(extension);

                // Open file as a stream
                using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    context.Response.ContentType = mimeType;
                    context.Response.ContentLength64 = fs.Length;
                    context.Response.StatusCode = 200;

                    byte[] buffer = new byte[4096];
                    int bytesRead;
                    while ((bytesRead = fs.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        context.Response.OutputStream.Write(buffer, 0, bytesRead);
                    }
                }

                context.Response.OutputStream.Close();
            }
            catch (Exception ex)
            {
                Log($"Error serving file: {ex.Message}");
                SendResponse(context.Response, "500 Internal Server Error", 500);
            }
        }


        // MIME Type Helper Function
        private string GetMimeType(string extension)
        {
            var mimeTypes = new Dictionary<string, string>
    {
        { ".html", "text/html" },
        { ".htm", "text/html" },
        { ".txt", "text/plain" },
        { ".jpg", "image/jpeg" },
        { ".jpeg", "image/jpeg" },
        { ".png", "image/png" },
        { ".gif", "image/gif" },
        { ".pdf", "application/pdf" },
        { ".zip", "application/zip" },
        { ".mp3", "audio/mpeg" },
        { ".mp4", "video/mp4" },
        { ".css", "text/css" },
        { ".js", "application/javascript" }
    };

            return mimeTypes.ContainsKey(extension) ? mimeTypes[extension] : "application/octet-stream";
        }


        private void SendResponse(HttpListenerResponse response, string content, int statusCode)
        {
            byte[] buffer = Encoding.UTF8.GetBytes(content);
            response.StatusCode = statusCode;
            response.ContentType = "text/html";
            response.ContentLength64 = buffer.Length;
            response.OutputStream.Write(buffer, 0, buffer.Length);
            response.OutputStream.Close();
        }

        private void Log(string message)
        {
            string logFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "server_log.txt");
            string logEntry = $"{DateTime.Now}: {message}{Environment.NewLine}";
            File.AppendAllText(logFile, logEntry);
        }

        private string GetLocalIPv4()
        {
            string localIP = "127.0.0.1";
            foreach (var ip in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
            {
                if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    localIP = ip.ToString();
                    break;
                }
            }
            return localIP;
        }

        private void txtFolderPath_Click(object sender, EventArgs e)
        {

        }

        private void lblStatus_Click(object sender, EventArgs e)
        {

        }

        private void button1_Click(object sender, EventArgs e)
        {
            Environment.Exit(0);
        }
    }
}
