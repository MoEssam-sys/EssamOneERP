using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using One.Infrastructure.Interfaces;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;

namespace One.Infrastructure.Services
{
    public class EmailService : IEmailService
    {
        private readonly IConfiguration _config;
        private readonly ILogger<EmailService> _logger;

        public EmailService(IConfiguration config, ILogger<EmailService> logger)
        {
            _config = config;
            _logger = logger;
        }

        public async Task SendEmailAsync(string to, string subject, string body)
        {
            var smtpHost = _config["Email:SmtpHost"];
            var smtpPort = _config.GetValue<int>("Email:SmtpPort", 587);
            var smtpUser = _config["Email:SmtpUser"];
            var smtpPass = _config["Email:SmtpPass"];
            var fromEmail = _config["Email:FromEmail"] ?? "noreply@oneerp.com";

            // If config is missing, just log (Debug Mode)
            if (string.IsNullOrEmpty(smtpHost))
            {
                _logger.LogWarning($"[Mock Email] To: {to}, Subject: {subject}, Body: {body}");
                return;
            }

            using var client = new SmtpClient(smtpHost, smtpPort)
            {
                Credentials = new NetworkCredential(smtpUser, smtpPass),
                EnableSsl = true
            };

            var mailMessage = new MailMessage
            {
                From = new MailAddress(fromEmail, "OneTech"),
                Subject = subject,
                Body = body,
                IsBodyHtml = true
            };

            mailMessage.To.Add(to);

            await client.SendMailAsync(mailMessage);
        }
    }
}
