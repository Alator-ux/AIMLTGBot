using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NeuralNetwork1;
using NeuralNetwork1.Dataset;
using NeuralNetwork1.ImageProcessor;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Extensions.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace AIMLTGBot
{
    public class TelegramService : IDisposable
    {
        private string systemPattern = "zwkvssqhqpbrgcgxtaxjneueftjcwuztqsakxmnzjcyyauasntayppiqtcjtytciwzpuktlojqepafmqkvnskbjttagbhffuirndtkjwcxfuayyubbluvpzsqzwmakadvhpagtiianhqfuthpjripz\r\n";
        private readonly TelegramBotClient client;
        private readonly AIMLService aiml;
        // CancellationToken - инструмент для отмены задач, запущенных в отдельном потоке
        private readonly CancellationTokenSource cts = new CancellationTokenSource();
        private BaseNetwork net;
        public string Username { get; }
        public TelegramService(string token, StudentNetwork net, AIMLService aimlService)
        {
            this.net = net;
            aiml = aimlService;
            client = new TelegramBotClient(token);
            client.StartReceiving(HandleUpdateMessageAsync, HandleErrorAsync, new ReceiverOptions
            {   // Подписываемся только на сообщения
                AllowedUpdates = new[] { UpdateType.Message }
            },
            cancellationToken: cts.Token);
            // Пробуем получить логин бота - тестируем соединение и токен
            Username = client.GetMeAsync().Result.Username;
        }

        async Task HandleUpdateMessageAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            var message = update.Message;
            var chatId = message.Chat.Id;
            var username = message.Chat.FirstName;
            if (message.Type == MessageType.Text)
            {
                var messageText = update.Message.Text;

                Console.WriteLine($"Received a '{messageText}' message in chat {chatId} with {username}.");

                // Echo received message text
                await botClient.SendTextMessageAsync(
                    //parseMode: ParseMode.MarkdownV2,
                    chatId: chatId,
                    text: aiml.Talk(chatId, username, messageText),
                    cancellationToken: cancellationToken);
                return;
            }
            // Загрузка изображений пригодится для соединения с нейросетью
            if (message.Type == MessageType.Photo)
            {
                var photoId = message.Photo.Last().FileId;
                Telegram.Bot.Types.File fl = await client.GetFileAsync(photoId, cancellationToken: cancellationToken);
                var imageStream = new MemoryStream();
                await client.DownloadFileAsync(fl.FilePath, imageStream, cancellationToken: cancellationToken);

                var bitmap = new Bitmap(Image.FromStream(imageStream));
                var pr = new NeuralNetwork1.ImageProcessor.Processor().processImage(bitmap);
                var letter = net.Predict(new Sample(pr, 10));
                var strLetter = MorseWrapper.MorseToString(letter);

                imageStream.Seek(0, 0);
                await botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: strLetter,
                    cancellationToken: cancellationToken);

                aiml.Talk(chatId, username, strLetter + " " + systemPattern);

                /*await client.SendPhotoAsync(
                    message.Chat.Id,
                    imageStream,
                    "Пока что я не знаю, что делать с картинками, так что держи обратно",
                    cancellationToken: cancellationToken
                );*/
                return;
            }
            // Можно обрабатывать разные виды сообщений, просто для примера пробросим реакцию на них в AIML
            if (message.Type == MessageType.Video)
            {
                await client.SendTextMessageAsync(message.Chat.Id, aiml.Talk(chatId, username, "Видео"), cancellationToken: cancellationToken);
                return;
            }
            if (message.Type == MessageType.Audio)
            {
                await client.SendTextMessageAsync(message.Chat.Id, aiml.Talk(chatId, username, "Аудио"), cancellationToken: cancellationToken);
                return;
            }
        }

        Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            var apiRequestException = exception as ApiRequestException;
            if (apiRequestException != null)
                Console.WriteLine($"Telegram API Error:\n[{apiRequestException.ErrorCode}]\n{apiRequestException.Message}");
            else
                Console.WriteLine(exception.ToString());
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            // Заканчиваем работу - корректно отменяем задачи в других потоках
            // Отменяем токен - завершатся все асинхронные таски
            cts.Cancel();
        }
    }
}
