using System.Net;
using ImmichFrame.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace ImmichFrame.Core.Tests.Services;

[TestFixture]
public class IcalCalendarServiceTests
{
    private const string Secret = "s3cr3t-token";

    private static string TodayCalendar(string summary)
    {
        var start = DateTime.Today.AddHours(12).ToString("yyyyMMdd'T'HHmmss");
        var end = DateTime.Today.AddHours(13).ToString("yyyyMMdd'T'HHmmss");
        return $"BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//test//EN\r\nBEGIN:VEVENT\r\nUID:{Guid.NewGuid()}\r\n" +
               $"DTSTART:{start}\r\nDTEND:{end}\r\nSUMMARY:{summary}\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";
    }

    private static (IcalCalendarService Service, ListLogger Logger) Create(List<string> calendars,
        Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var settings = new Mock<IGeneralSettings>();
        settings.SetupGet(s => s.Webcalendars).Returns(calendars);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(new Handler(respond)));
        var logger = new ListLogger();
        return (new IcalCalendarService(settings.Object, logger, factory.Object), logger);
    }

    [Test]
    public async Task OneUnreachableCalendar_DoesNotHideTheOthers()
    {
        var (service, logger) = Create(
            [$"https://down.example/private/{Secret}/basic.ics", "https://up.example/cal.ics", "not a url"],
            request => request.RequestUri!.Host == "down.example"
                ? throw new HttpRequestException("connection refused")
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(TodayCalendar("Lunch")) });

        var appointments = await service.GetAppointments();

        Assert.That(appointments.Select(a => a.Summary), Is.EqualTo(new[] { "Lunch" }));
        Assert.That(logger.Messages, Has.Some.Contains("down.example"));
        Assert.That(logger.Messages, Has.None.Contains(Secret));
        Assert.That(logger.Messages, Has.None.Contains("not a url"));
    }

    [Test]
    public async Task FailedStatus_LogsHostOnly()
    {
        var (service, logger) = Create(
            [$"https://user:pa55@cal.example/{Secret}.ics?token={Secret}"],
            _ => new HttpResponseMessage(HttpStatusCode.Forbidden));

        var appointments = await service.GetAppointments();

        Assert.That(appointments, Is.Empty);
        Assert.That(logger.Messages, Has.Some.Contains("cal.example"));
        Assert.That(logger.Messages, Has.None.Contains(Secret));
        Assert.That(logger.Messages, Has.None.Contains("pa55"));
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(respond(request));
    }

    private sealed class ListLogger : ILogger<IcalCalendarService>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception) + (exception != null ? " " + exception : ""));
        }
    }
}
