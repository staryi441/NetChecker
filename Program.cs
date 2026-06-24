using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;

class Program
{
    private static readonly string Version = "1.1.0"; 
    private static readonly string GitHubRepo = "staryi441/NetChakerList"; 
    private static readonly string ListDir = "list";

    private static readonly List<string> RemoteDomainFiles = new() 
    { 
        "list-general.txt", "list-google.txt", "list-discord.txt", "list-telegram.txt", "list-user.txt" 
    };
    
    private static readonly List<string> RemoteIpFiles = new() 
    { 
        "ipset-general.txt", "ipset-google.txt", "ipset-discord.txt", "ipset-telegram.txt", "ipset-user.txt" 
    };

    private static readonly Dictionary<string, List<string>> FailedDomainsGrouped = new();
    private static readonly Dictionary<string, List<string>> FailedIpsGrouped = new();

    static async Task Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        if (args.Length > 0 && args[0] == "--update-finalize")
        {
            FinalizeUpdate();
            return;
        }

        Console.Title = $"NetChecker v{Version}";
        EnsureStructureExists();

        bool keepRunning = true;
        while (keepRunning)
        {
            Console.Clear();
            DrawMenu();

            Console.Write("\nВыберите пункт меню (0-4): ");
            string? input = Console.ReadLine()?.Trim();

            Console.Clear();
            switch (input)
            {
                case "1":
                    await RunProtocolTests();
                    ShowExitPrompt();
                    break;
                case "2":
                    FailedDomainsGrouped.Clear(); 
                    await RunDynamicTests(isDomainTest: true);
                    PrintGroupedReport(isDomainTest: true);
                    ShowExitPrompt();
                    break;
                case "3":
                    FailedIpsGrouped.Clear();
                    await RunDynamicTests(isDomainTest: false);
                    PrintGroupedReport(isDomainTest: false);
                    ShowExitPrompt();
                    break;
                case "4":
                    await CheckAndRunUpdates();
                    ShowExitPrompt();
                    break;
                case "0":
                    keepRunning = false;
                    break;
                default:
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Неверный ввод. Пожалуйста, введите цифру от 0 до 4.");
                    Console.ResetColor();
                    Thread.Sleep(1500);
                    break;
            }
        }
    }

    private static void DrawMenu()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"NETCHECKER MANAGER v{Version}");
        Console.WriteLine("----------------------------------------------------");
        Console.ResetColor();

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine(":: ПРОВЕРКА ДОСТУПНОСТИ");
        Console.ResetColor();
        Console.WriteLine("  1) Тест сетевых протоколов (TCP/UDP/QUIC)");
        Console.WriteLine($"  2) Тест блокировок доменов (файлы `list-*.txt`)");
        Console.WriteLine($"  3) Тест блокировок IP/Подсетей (файлы `ipset-*.txt` IPv4/IPv6 CIDR)");
        
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("\n:: ОБНОВЛЕНИЯ И ОБСЛУЖИВАНИЕ");
        Console.ResetColor();
        Console.WriteLine("  4) Проверить обновления (Программы и списков из репозитория)");

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("----------------------------------------------------");
        Console.ResetColor();
        Console.WriteLine("  0) Выход");
    }

    private static void ShowExitPrompt()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\nНажмите любую клавишу для возврата в главное меню...");
        Console.ResetColor();
        Console.ReadKey();
    }

    private static void EnsureStructureExists()
    {
        if (!Directory.Exists(ListDir)) Directory.CreateDirectory(ListDir);
    }

    private static void PrintGroupedReport(bool isDomainTest)
    {
        Console.WriteLine("\n====================================================");
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("ИТОГОВЫЙ ОТЧЕТ: ОБНАРУЖЕННЫЕ ПРОБЛЕМЫ (ПО СПИСКАМ)");
        Console.ResetColor();
        Console.WriteLine("====================================================");

        var targetGrouped = isDomainTest ? FailedDomainsGrouped : FailedIpsGrouped;
        int totalFailedCount = targetGrouped.Sum(x => x.Value.Count);

        if (totalFailedCount == 0)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(isDomainTest 
                ? "Все проверенные домены во всех списках полностью доступны по всем тестам!" 
                : "Все проверенные IP-адреса и подсети во всех списках абсолютно доступны!");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Всего обнаружено частично или полностью неработающих позиций: {totalFailedCount}\n");
            Console.ResetColor();

            foreach (var group in targetGrouped.Where(g => g.Value.Count > 0))
            {
                Console.ForegroundColor = ConsoleColor.Magenta;
                Console.WriteLine($"• Список [{group.Key.ToUpper()}]:");
                Console.ResetColor();

                foreach (var failedItem in group.Value)
                {
                    Console.ForegroundColor = ConsoleColor.DarkRed;
                    Console.WriteLine($"  ├── [X] {failedItem}");
                }
                Console.ResetColor();
                Console.WriteLine(); 
            }
        }
        Console.WriteLine("====================================================");
    }

    private static async Task CheckAndRunUpdates()
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("--- ПРОВЕРКА ОБНОВЛЕНИЙ GITHUB ---");
        Console.ResetColor();

        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("NetChecker-Updater", Version));

        string apiUrl = $"https://api.github.com/repos/{GitHubRepo}/releases/latest";
        
        try
        {
            Console.WriteLine("Запрос метаданных последнего релиза через GitHub API...");
            var response = await client.GetAsync(apiUrl);
            
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Бинарных релизов на GitHub пока не найдено. Переходим к синхронизации списков.");
                Console.ResetColor();
            }
            else if (!response.IsSuccessStatusCode)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Ошибка GitHub API: {response.StatusCode}");
            }
            else
            {
                string json = await response.Content.ReadAsStringAsync();
                string tagPrefix = "\"tag_name\":\"";
                int tagIdx = json.IndexOf(tagPrefix);
                
                if (tagIdx != -1)
                {
                    int tagEndIdx = json.IndexOf("\"", tagIdx + tagPrefix.Length);
                    string latestVersion = json.Substring(tagIdx + tagPrefix.Length, tagEndIdx - (tagIdx + tagPrefix.Length)).Replace("v", "");

                    Console.WriteLine($"Текущая версия программы: {Version}");
                    Console.WriteLine($"Последняя версия в репозитории: {latestVersion}");

                    if (latestVersion != Version)
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("\n[!] Доступна более новая версия программы!");
                        Console.ResetColor();
                        Console.Write("Хотите обновить NetChecker.exe? (y/n): ");
                        string? choice = Console.ReadLine()?.Trim().ToLower();

                        if (choice == "y" || choice == "yes")
                        {
                            string downloadUrlPrefix = "\"browser_download_url\":\"";
                            int assetIdx = 0;
                            string exeDownloadUrl = "";

                            while ((assetIdx = json.IndexOf(downloadUrlPrefix, assetIdx)) != -1)
                            {
                                int endUrlIdx = json.IndexOf("\"", assetIdx + downloadUrlPrefix.Length);
                                string url = json.Substring(assetIdx + downloadUrlPrefix.Length, endUrlIdx - (assetIdx + downloadUrlPrefix.Length));
                                
                                if (url.EndsWith("NetChecker.exe"))
                                {
                                    exeDownloadUrl = url;
                                    break;
                                }
                                assetIdx = endUrlIdx;
                            }

                            if (!string.IsNullOrEmpty(exeDownloadUrl))
                            {
                                await DownloadAndApplyUpdate(client, exeDownloadUrl);
                            }
                            else
                            {
                                Console.ForegroundColor = ConsoleColor.Red;
                                Console.WriteLine("Ошибка: Скомпилированный файл NetChecker.exe не найден в релизе.");
                            }
                        }
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("Исполняемый файл программы имеет актуальную версию.");
                        Console.ResetColor();
                    }
                }
            }

            await UpdateOfficialLists(client);
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Сетевая ошибка при обновлении: {ex.Message}");
        }
    }

    private static async Task DownloadAndApplyUpdate(HttpClient client, string downloadUrl)
    {
        try
        {
            string currentExePath = Environment.ProcessPath ?? "NetChecker.exe";
            string backupExePath = currentExePath + ".bak";

            Console.WriteLine("Скачивание новой сборки...");
            byte[] newExeBytes = await client.GetByteArrayAsync(downloadUrl);

            if (File.Exists(backupExePath)) File.Delete(backupExePath);
            File.Move(currentExePath, backupExePath);

            await File.WriteAllBytesAsync(currentExePath, newExeBytes);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Файл успешно заменен! Перезапуск процесса...");
            
            Process.Start(new ProcessStartInfo
            {
                FileName = currentExePath,
                Arguments = "--update-finalize",
                UseShellExecute = true
            });

            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Не удалось заменить исполняемый файл: {ex.Message}");
        }
    }

    private static void FinalizeUpdate()
    {
        Thread.Sleep(1200);
        string currentExePath = Environment.ProcessPath ?? "NetChecker.exe";
        string backupExePath = currentExePath + ".bak";

        try
        {
            if (File.Exists(backupExePath)) File.Delete(backupExePath);
        }
        catch { }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Обновление завершено! Временные файлы развертывания успешно удалены.");
        Console.ResetColor();
        Thread.Sleep(1500);
    }

    private static async Task UpdateOfficialLists(HttpClient client)
    {
        Console.WriteLine("\nСинхронизация списков конфигурации из репозитория...");
        string rawBaseUrl = $"https://raw.githubusercontent.com/{GitHubRepo}/main/list/";

        List<string> allRemoteFiles = new();
        allRemoteFiles.AddRange(RemoteDomainFiles);
        allRemoteFiles.AddRange(RemoteIpFiles);

        foreach (string fileName in allRemoteFiles)
        {
            string localPath = Path.Combine(ListDir, fileName);

            if (fileName.Contains("-user.txt") && File.Exists(localPath))
            {
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.WriteLine($" -> Пропущен (Сохранен пользовательский файл): {fileName}");
                Console.ResetColor();
                continue;
            }

            try
            {
                string url = rawBaseUrl + fileName;
                var res = await client.GetAsync(url);
                
                if (res.IsSuccessStatusCode)
                {
                    string content = await res.Content.ReadAsStringAsync();
                    await File.WriteAllTextAsync(localPath, content);
                    Console.WriteLine($" -> Синхронизирован: {fileName}");
                }
                else
                {
                    if (fileName.Contains("-user.txt"))
                    {
                        if (!File.Exists(localPath)) await File.WriteAllTextAsync(localPath, "");
                        Console.WriteLine($" -> Создан пустой локальный шаблон: {fileName}");
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.DarkYellow;
                        Console.WriteLine($" -> Отсутствует в удаленном репозитории: {fileName}");
                        Console.ResetColor();
                    }
                }
            }
            catch
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($" -> Ошибка при загрузке файла: {fileName}");
                Console.ResetColor();
            }
        }
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Локальная структура каталогов синхронизирована с веткой main.");
        Console.ResetColor();
    }

    private static async Task RunDynamicTests(bool isDomainTest)
    {
        string filterPattern = isDomainTest ? "list-*.txt" : "ipset-*.txt";
        string typeLabel = isDomainTest ? "ДОМЕНЫ" : "IP-АДРЕСА И ПОДСЕТИ";

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"--- СКАНИРОВАНИЕ РАБОЧЕЙ ОБЛАСТИ \\{ListDir} ({typeLabel}) ---");
        Console.ResetColor();

        if (!Directory.Exists(ListDir)) return;

        string[] files = Directory.GetFiles(ListDir, filterPattern);

        if (files.Length == 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"В рабочей области не найдено файлов конфигурации, соответствующих {filterPattern}.");
            Console.ResetColor();
            return;
        }

        foreach (string filePath in files)
        {
            string listName = Path.GetFileNameWithoutExtension(filePath);
            
            if (isDomainTest) FailedDomainsGrouped[listName] = new List<string>();
            else FailedIpsGrouped[listName] = new List<string>();

            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine($"\n[Список: {listName.ToUpper()}] (Файл: {Path.GetFileName(filePath)})");
            Console.WriteLine("----------------------------------------------------");
            Console.ResetColor();

            if (isDomainTest)
                await ExecuteDomainTestFile(filePath, listName);
            else
                await ExecuteIpTestFile(filePath, listName);
        }
    }

    private static async Task ExecuteDomainTestFile(string filePath, string listName)
    {
        string[] lines = await File.ReadAllLinesAsync(filePath);
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");

        foreach (var line in lines)
        {
            string cleanDomain = line.Trim();
            if (string.IsNullOrWhiteSpace(cleanDomain) || cleanDomain.StartsWith("#")) continue;

            string pingHost = cleanDomain.Replace("https://", "").Replace("http://", "").Split('/')[0];

            var pingResult = await TestIcmpPingQuietAsync(pingHost);
            var webWatch = Stopwatch.StartNew();
            bool webPassed = false;
            string webStatusErrorMessage = "Неизвестная ошибка";

            string url = cleanDomain.StartsWith("http") ? cleanDomain : $"https://{cleanDomain}";
            try
            {
                var response = await client.GetAsync(url);
                webWatch.Stop();
                webPassed = true;
                webStatusErrorMessage = $"HTTP {(int)response.StatusCode} ({webWatch.ElapsedMilliseconds} мс)";
            }
            catch (HttpRequestException ex)
            {
                webWatch.Stop();
                if (ex.InnerException is SocketException sex)
                {
                    if (sex.SocketErrorCode == SocketError.ConnectionReset)
                        webStatusErrorMessage = $"БЛОКИРОВКА DPI (TCP RST на {webWatch.ElapsedMilliseconds}мс)";
                    else if (sex.SocketErrorCode == SocketError.TimedOut)
                        webStatusErrorMessage = "БЛОКИРОВКА DROP (Тайм-аут соединения)";
                    else
                        webStatusErrorMessage = $"НЕДОСТУПЕН ({sex.Message})";
                }
                else
                {
                    webStatusErrorMessage = "БЛОКИРОВКА / ПОДМЕНА (Ошибка валидации SSL/TLS)";
                }
            }
            catch (TaskCanceledException)
            {
                webWatch.Stop();
                webStatusErrorMessage = "НЕДОСТУПЕН (Истек общий тайм-аут запроса)";
            }

            // Главная строка статуса домена
            if (pingResult.Success || webPassed)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write($"\n[V] {cleanDomain} -> ДОСТУПЕН");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write($"\n[X] {cleanDomain} -> ЗАБЛОКИРОВАН / НЕДОСТУПЕН");
            }
            Console.ResetColor();

            // Подробности тестов
            Console.Write("\n   ICMP Пинг: ");
            if (pingResult.Success) 
            { 
                Console.ForegroundColor = ConsoleColor.Green; 
                Console.WriteLine($"УСПЕШНО ({pingResult.Ms} мс)"); 
            }
            else 
            { 
                Console.ForegroundColor = ConsoleColor.Red; 
                Console.WriteLine("FAILED / ТАЙМ-АУТ"); 
            }
            Console.ResetColor();

            Console.Write("   Веб-доступ (HTTPS): ");
            if (webPassed) { Console.ForegroundColor = ConsoleColor.Green; Console.WriteLine($"{webStatusErrorMessage}"); }
            else { Console.ForegroundColor = ConsoleColor.Red; Console.WriteLine($"{webStatusErrorMessage}"); }
            Console.ResetColor();

            // Попадание в отчёт
            if (!pingResult.Success && !webPassed)
            {
                FailedDomainsGrouped[listName].Add($"{cleanDomain} (Полная недоступность: Пинг упал + Веб сброшен)");
            }
            else if (!webPassed)
            {
                FailedDomainsGrouped[listName].Add($"{cleanDomain} (Частичная блокировка: ICMP Пинг прошел, но Веб заблокирован DPI/Сброшен)");
            }
            else if (!pingResult.Success)
            {
                FailedDomainsGrouped[listName].Add($"{cleanDomain} (Частичная блокировка: Веб-доступ OK, но ICMP Пакеты полностью фильтруются)");
            }
        }
    }

    private static async Task ExecuteIpTestFile(string filePath, string listName)
    {
        string[] lines = await File.ReadAllLinesAsync(filePath);

        foreach (var line in lines)
        {
            string cleanLine = line.Trim();
            if (string.IsNullOrWhiteSpace(cleanLine) || cleanLine.StartsWith("#")) continue;

            if (cleanLine.Contains('/'))
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"\n-> Парсинг и обработка подсети: {cleanLine}");
                Console.ResetColor();

                using var semaphore = new SemaphoreSlim(32);
                var tasks = new List<Task>();
                int counter = 0;

                foreach (var ip in ManualParseCidrLazy(cleanLine))
                {
                    counter++;
                    await semaphore.WaitAsync();

                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            await TestSingleIpAddressAsync(ip, cleanLine, listName);
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }));
                }

                if (tasks.Count > 0)
                {
                    await Task.WhenAll(tasks);
                    Console.WriteLine($"   Сканирование подсети завершено. Проверено целей: {counter}");
                }
            }
            else
            {
                if (IPAddress.TryParse(cleanLine, out IPAddress? address))
                {
                    await TestSingleIpAddressAsync(address, cleanLine, listName);
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.DarkYellow;
                    Console.WriteLine($"\n[X] Элемент '{cleanLine}' пропущен. Неверный синтаксис.");
                    Console.ResetColor();
                }
            }
        }
    }

    private static async Task TestSingleIpAddressAsync(IPAddress currentIp, string originContext, string listName)
    {
        string hostString = currentIp.ToString();
        string ipType = currentIp.AddressFamily == AddressFamily.InterNetworkV6 ? "IPv6" : "IPv4";
        string logContext = originContext.Contains('/') ? $"{hostString} (из {originContext})" : hostString;

        var pingTask = TestIcmpPingQuietAsync(hostString);
        var tcpTask = TestTcpConnectionQuietAsync(hostString, 443);

        await Task.WhenAll(pingTask, tcpTask);

        var pingResult = pingTask.Result;
        var tcpResult = tcpTask.Result;

        // Главная строка статуса IP-адреса
        if (pingResult.Success || tcpResult.Success)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"\n[V] {hostString} -> ДОСТУПЕН");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n[X] {hostString} -> ЗАБЛОКИРОВАН / НЕДОСТУПЕН");
        }
        Console.ResetColor();

        // Детализация тестов для IP-адреса
        Console.Write("   ICMP Пинг: ");
        if (pingResult.Success) 
        { 
            Console.ForegroundColor = ConsoleColor.Green; 
            Console.WriteLine($"УСПЕШНО ({pingResult.Ms} мс)"); 
        }
        else 
        { 
            Console.ForegroundColor = ConsoleColor.Red; 
            Console.WriteLine("FAILED / ТАЙМ-АУТ"); 
        }
        Console.ResetColor();

        Console.Write("   Порт 443 (TCP): ");
        if (tcpResult.Success) 
        { 
            Console.ForegroundColor = ConsoleColor.Green; 
            Console.WriteLine($"ОТКРЫТ / ДОСТУПЕН ({tcpResult.Ms} мс)"); 
        }
        else 
        { 
            Console.ForegroundColor = ConsoleColor.Red; 
            Console.WriteLine("ЗАКРЫТ (DPI Firewall Drop/RST)"); 
        }
        Console.ResetColor();

        // КРИТЕРИИ ДЛЯ ИТОГОВОГО ОТЧЕТА ПО IP
        if (!pingResult.Success && !tcpResult.Success)
        {
            lock (FailedIpsGrouped) 
            { 
                FailedIpsGrouped[listName].Add($"[{ipType}] {logContext} (Ресурс полностью недоступен по всем протоколам)"); 
            }
        }
        else if (!tcpResult.Success)
        {
            lock (FailedIpsGrouped)
            {
                FailedIpsGrouped[listName].Add($"[{ipType}] {logContext} (Порт 443 сброшен инфраструктурой DPI, хотя ICMP Пинг идет)");
            }
        }
        else if (!pingResult.Success)
        {
            lock (FailedIpsGrouped)
            {
                FailedIpsGrouped[listName].Add($"[{ipType}] {logContext} (Порт 443 открыт, но ICMP Пинг блокируется/фильтруется дропом)");
            }
        }
    }

    private static IEnumerable<IPAddress> ManualParseCidrLazy(string cidr)
    {
        string[] parts = cidr.Split('/');
        if (parts.Length != 2) yield break;

        if (!IPAddress.TryParse(parts[0], out IPAddress? baseAddress) || !int.TryParse(parts[1], out int prefixLength))
            yield break;

        byte[] baseBytes = baseAddress.GetAddressBytes();

        if (baseAddress.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (prefixLength < 120)
            {
                yield return baseAddress;
                for (int i = 1; i <= 4; i++) yield return IncrementIP(baseAddress, i);
            }
            else
            {
                int hostBits = 128 - prefixLength;
                long hostCount = 1L << hostBits;
                for (long i = 0; i < hostCount; i++) yield return IncrementIP(baseAddress, (int)i);
            }
            yield break;
        }

        if (prefixLength < 0 || prefixLength > 32) yield break;

        uint mask = prefixLength == 0 ? 0 : uint.MaxValue << (32 - prefixLength);
        uint ipNetworkAddr = ((uint)baseBytes[0] << 24) | ((uint)baseBytes[1] << 16) | ((uint)baseBytes[2] << 8) | baseBytes[3];
        ipNetworkAddr &= mask; 

        int ipv4HostBits = 32 - prefixLength;
        long totalHosts = 1L << ipv4HostBits;

        if (ipv4HostBits > 8)
        {
            for (uint i = 1; i <= 64; i++)
            {
                yield return UIntToIP(ipNetworkAddr + i);
            }
            for (uint i = 65; i >= 2; i--)
            {
                yield return UIntToIP((uint)(ipNetworkAddr + totalHosts - i));
            }
        }
        else
        {
            for (uint i = 0; i < totalHosts; i++)
            {
                yield return UIntToIP(ipNetworkAddr + i);
            }
        }
    }

    private static IPAddress UIntToIP(uint ipLong)
    {
        return new IPAddress(new byte[] {
            (byte)((ipLong >> 24) & 0xFF),
            (byte)((ipLong >> 16) & 0xFF),
            (byte)((ipLong >> 8) & 0xFF),
            (byte)(ipLong & 0xFF)
        });
    }

    private static IPAddress IncrementIP(IPAddress baseAddress, int value)
    {
        byte[] bytes = baseAddress.GetAddressBytes();
        bytes[bytes.Length - 1] = (byte)(bytes[bytes.Length - 1] + value);
        return new IPAddress(bytes);
    }

    private static async Task RunProtocolTests()
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("--- [1] ДИАГНОСТИКА БАЗОВЫХ СЕТЕВЫХ ПРОТОКОЛОВ ---");
        Console.ResetColor();

        await TestTcpConnection("1.1.1.1", 443);
        await TestUdpConnection("8.8.8.8", 53);
        await TestHttp3Connection("https://cloudflare.com");
    }

    private static async Task<bool> TestTcpConnection(string host, int port, bool quietMode = false)
    {
        if (!quietMode) Console.Write($"TCP (Порт {port}) -> ");
        using var client = new TcpClient(AddressFamily.InterNetworkV6);
        client.Client.DualMode = true; 
        
        var watch = Stopwatch.StartNew();
        
        try
        {
            var connectTask = client.ConnectAsync(host, port);
            var completedTask = await Task.WhenAny(connectTask, Task.Delay(3000));

            if (completedTask != connectTask)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("ЗАБЛОКИРОВАН: Запрос сброшен файрволом (Тайм-аут).");
                Console.ResetColor();
                return false;
            }

            watch.Stop();
            await Task.Delay(200); 

            if (client.Client.Poll(1000, SelectMode.SelectRead) && client.Client.Available == 0)
            {
                Console.ForegroundColor = ConsoleColor.DarkRed;
                Console.WriteLine($"СОЕДИНЕНИЕ СОРВАНО (Перехвачен TCP RST от DPI на {watch.ElapsedMilliseconds}мс).");
                Console.ResetColor();
                return false;
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"ДОСТУПЕН ({watch.ElapsedMilliseconds} мс).");
                Console.ResetColor();
                return true;
            }
        }
        catch (SocketException ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            if (ex.SocketErrorCode == SocketError.ConnectionRefused)
                Console.WriteLine("ОТКЛОНЕНО (В соединении отказано целевым хостом).");
            else
                Console.WriteLine($"ОШИБКА: {ex.Message}");
            Console.ResetColor();
            return false;
        }
    }

    private static async Task<(bool Success, long Ms)> TestTcpConnectionQuietAsync(string host, int port)
    {
        using var client = new TcpClient(AddressFamily.InterNetworkV6);
        client.Client.DualMode = true;
        var watch = Stopwatch.StartNew();
        try
        {
            var connectTask = client.ConnectAsync(host, port);
            var completedTask = await Task.WhenAny(connectTask, Task.Delay(2000));
            if (completedTask != connectTask) return (false, 0);

            await Task.Delay(100);
            if (client.Client.Poll(1000, SelectMode.SelectRead) && client.Client.Available == 0) return (false, 0);

            watch.Stop();
            return (true, watch.ElapsedMilliseconds);
        }
        catch { return (false, 0); }
    }

    private static async Task TestUdpConnection(string host, int port)
    {
        Console.Write($"UDP (Порт {port}) -> ");
        using var udpClient = new UdpClient(AddressFamily.InterNetworkV6);
        udpClient.Client.DualMode = true;
        try
        {
            udpClient.Connect(host, port);
            byte[] sendBytes = new byte[] { 0x00, 0x00, 0x01, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x03, 0x77, 0x77, 0x77, 0x06, 0x67, 0x6f, 0x6f, 0x67, 0x6c, 0x65, 0x03, 0x63, 0x6f, 0x6d, 0x00, 0x00, 0x01, 0x00, 0x01 };
            await udpClient.SendAsync(sendBytes, sendBytes.Length);
            
            var receiveTask = udpClient.ReceiveAsync();
            var completedTask = await Task.WhenAny(receiveTask, Task.Delay(2500));

            if (completedTask == receiveTask)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("ДОСТУПЕН (Получен валидный ответ на UDP-датаграмму).");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("ФИЛЬТРАЦИЯ / ТАЙМ-АУТ (Пакет ушел, проверочный ответ не вернулся).");
            }
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"ИСКЛЮЧЕНИЕ UDP SOCKET: {ex.Message}");
        }
        finally
        {
            Console.ResetColor();
        }
    }

    private static async Task TestHttp3Connection(string url)
    {
        Console.Write("QUIC / HTTP3 -> ");
        var handler = new SocketsHttpHandler();
        using var httpClient = new HttpClient(handler);
        
        var request = new HttpRequestMessage(HttpMethod.Get, url)
        {
            Version = HttpVersion.Version30,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact
        };

        try
        {
            var response = await httpClient.SendAsync(request);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"ДОСТУПЕН (Проверено через HTTP/3. Статус: {response.StatusCode}).");
        }
        catch (Exception)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("ЗАБЛОКИРОВАН (Транспорт QUIC/HTTP3 подавляется активным DPI).");
        }
        finally
        {
            Console.ResetColor();
        }
    }

    private static async Task<(bool Success, long Ms)> TestIcmpPingQuietAsync(string host)
    {
        using var ping = new Ping();
        try
        {
            var watch = Stopwatch.StartNew();
            PingReply reply = await ping.SendPingAsync(host, 1500);
            watch.Stop();
            
            if (reply.Status == IPStatus.Success)
            {
                return (true, reply.RoundtripTime);
            }
            return (false, 0);
        }
        catch { return (false, 0); }
    }
}