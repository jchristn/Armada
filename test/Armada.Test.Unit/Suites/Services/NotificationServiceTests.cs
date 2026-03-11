namespace Armada.Test.Unit.Suites.Services
{
    using Armada.Core.Services;
    using Armada.Test.Common;

    public class NotificationServiceTests : TestSuite
    {
        public override string Name => "Notification Service";

        protected override async Task RunTestsAsync()
        {
            await RunTest("Bell DoesNotThrow", () =>
            {
                NotificationService.Bell();
            });

            await RunTest("Send DoesNotThrow", () =>
            {
                NotificationService.Send("Test Title", "Test Message");
            });

            await RunTest("Send WithSpecialCharacters DoesNotThrow", () =>
            {
                NotificationService.Send("Test's \"Title\"", "Message with 'quotes' and \"doubles\"");
            });

            await RunTest("Send EmptyStrings DoesNotThrow", () =>
            {
                NotificationService.Send("", "");
            });
        }
    }
}
