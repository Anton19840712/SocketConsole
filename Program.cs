using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Xml.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using SocketConsole.Models;

// Настройка логгера
var builder = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);

var configuration = builder.Build();

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

Log.Logger.Information("Application starting");

// Создание хоста и запуск фонового сервиса
var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        services.AddHostedService<BackgroundWorkerService>();
    })
    .UseSerilog()
    .Build();

await host.RunAsync();

// Класс фоновой службы
public class BackgroundWorkerService : BackgroundService
{
    public Card112ChangedRequest DeserializeXmlToCard112(string xmlMessage)
    {
        XmlSerializer serializer = new XmlSerializer(typeof(SoapEnvelope));

        using (TextReader reader = new StringReader(xmlMessage))
        {
            var envelope = (SoapEnvelope)serializer.Deserialize(reader);
            return envelope.Body.Card112ChangedRequest;
        }
    }
    string ExtractXmlFromMessage(string message)
    {
        // Ищем начало XML-данных после заголовков HTTP
        int xmlStartIndex = message.IndexOf("<?xml");
        if (xmlStartIndex >= 0)
        {
            return message.Substring(xmlStartIndex);
        }

        throw new InvalidOperationException("XML данных не найдено в сообщении");
    }


    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        IPAddress ipAddress = IPAddress.Any; // Можно указать конкретный IP, если требуется
        int port = 6295; // Порт для прослушивания

        TcpListener listener = new TcpListener(ipAddress, port);
        listener.Start();
        Log.Information("Server is running. Waiting for connections...");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                TcpClient client = await listener.AcceptTcpClientAsync(stoppingToken);
                Log.Information("Клиент подключен!");

                NetworkStream stream = client.GetStream();

                // Чтение сообщений от клиента:
                byte[] buffer = new byte[1024];
                int bytesRead;
                StringBuilder messageBuilder = new StringBuilder();

                // Читаем данные до тех пор, пока все сообщение не будет получено
                while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, stoppingToken)) > 0)
                {
                    string messagePart = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    messageBuilder.Append(messagePart);

                    // Проверяем, есть ли "Content-Length" в заголовке
                    if (messageBuilder.ToString().Contains("\r\n\r\n"))
                    {
                        // Отделяем заголовки от тела
                        string headers = messageBuilder.ToString().Split("\r\n\r\n")[0];
                        if (headers.Contains("Content-Length"))
                        {
                            int contentLength = int.Parse(headers
                                .Split("\r\n")
                                .FirstOrDefault(h => h.StartsWith("Content-Length:"))
                                ?.Split(":")[1].Trim());

                            // Если мы считали все байты тела сообщения, выходим из цикла
                            if (messageBuilder.Length >= contentLength + headers.Length + 4)
                            {
                                break;
                            }
                        }
                    }
                }

                string message = messageBuilder.ToString();
                Log.Information($"Сообщение от клиента: {message}");

                // Извлекаем XML из сообщения
                try
                {
                    string xmlMessage = ExtractXmlFromMessage(message);

                    // Десериализация сообщения
                    var cardRequest = DeserializeXmlToCard112(xmlMessage);
                    Log.Information($"Card ID: {cardRequest.EmergencyCardId}, Creator: {cardRequest.Creator}");
                }
                catch (Exception ex)
                {
                    Log.Error($"Ошибка: {ex.Message}");
                }

                // Закрываем соединение:
                client.Close();
            }
            catch (Exception ex)
            {
                Log.Error($"Ошибка: {ex.Message}");
            }
        }
    }
}