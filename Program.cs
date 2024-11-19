using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Xml.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using SocketConsole.Models;



Console.Title = "server";
// Настройка логгера
var builder = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);

var configuration = builder.Build();

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
	.WriteTo.Seq("https://seq.pit.protei.ru/")
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
    private IConnection _connection;
    private IModel _channel;

    public BackgroundWorkerService()
    {
        // Настройка RabbitMQ
        var factory = new ConnectionFactory
        {
            Uri = new Uri("AMQP://admin:admin@172.16.211.18/termidesk")
        };

        _connection = factory.CreateConnection();
        _channel = _connection.CreateModel();

        try
        {
            // Декларация очереди для получения сообщений
            _channel.QueueDeclare(queue: "from_bpmn_queue",
                                 durable: true,
                                 exclusive: false,
                                 autoDelete: false,
                                 arguments: null);

            // Декларация очереди для отправки сообщений
            _channel.QueueDeclare(queue: "to_bpmn_queue",
                                 durable: true,
                                 exclusive: false,
                                 autoDelete: false,
                                 arguments: null);
        }
        catch (Exception ex)
        {
            Log.Error($"Error occured:{ex.Message}");
            throw;
        }
       
    }

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

        // Начинаем прослушивание сообщений из RabbitMQ
        StartListeningToRabbitMq();

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

                    // Отправка сообщения в RabbitMQ
                    PublishMessage(cardRequest);

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

    private void StartListeningToRabbitMq()
    {
        // Создание обработчика сообщений из RabbitMQ
        var consumer = new EventingBasicConsumer(_channel);
        consumer.Received += (model, ea) =>
        {
            var body = ea.Body.ToArray();
            var message = Encoding.UTF8.GetString(body);
            Log.Information($"Received from broker: {message}");
        };

        _channel.BasicConsume(queue: "from_bpmn_queue",
                             autoAck: true,
                             consumer: consumer);
    }

    private void PublishMessage(Card112ChangedRequest cardRequest)
    {
        var body = Encoding.UTF8.GetBytes(cardRequest.ToString()); // Преобразуйте объект в строку или JSON

        _channel.BasicPublish(exchange: "",
                             routingKey: "to_bpmn_queue",
                             basicProperties: null,
                             body: body);
        Log.Information($"Message published to in RabbitMq to_bpmn_queue");
    }

    public override void Dispose()
    {
        _channel.Close();
        _connection.Close();
        base.Dispose();
    }
}
