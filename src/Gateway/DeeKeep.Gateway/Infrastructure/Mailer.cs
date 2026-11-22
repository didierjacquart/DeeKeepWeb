using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace DeeKeep.Gateway.Infrastructure;

/// <summary>
/// Section <c>Mail</c> de la configuration (dans <c>secrets.json</c> : le mot de passe y est).
/// Gmail : <c>smtp.gmail.com</c>, port 587, mot de passe d'application du compte.
/// </summary>
public sealed class MailOptions
{
    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public string? User { get; set; }
    public string? Password { get; set; }
    /// <summary>Adresse d'expédition ; par défaut <see cref="User"/>.</summary>
    public string? From { get; set; }
    public string FromName { get; set; } = "DeeKeep";
}

public interface IMailer
{
    bool Enabled { get; }

    Task SendAsync(string to, string subject, string body, CancellationToken ct);
}

/// <summary>Envoi SMTP (STARTTLS) des codes et des avis d'inscription.</summary>
public sealed class SmtpMailer(IOptionsMonitor<MailOptions> options) : IMailer
{
    public bool Enabled
    {
        get
        {
            var o = options.CurrentValue;
            return !string.IsNullOrWhiteSpace(o.Host) && !string.IsNullOrWhiteSpace(o.User) && !string.IsNullOrWhiteSpace(o.Password);
        }
    }

    public async Task SendAsync(string to, string subject, string body, CancellationToken ct)
    {
        var o = options.CurrentValue;
        if (!Enabled) throw new InvalidOperationException("Envoi d'e-mails non configuré (section Mail).");
        using var message = new MailMessage(new MailAddress(o.From ?? o.User!, o.FromName), new MailAddress(to))
        {
            Subject = subject,
            Body = body,
            BodyEncoding = System.Text.Encoding.UTF8,
            SubjectEncoding = System.Text.Encoding.UTF8,
        };
        using var client = new SmtpClient(o.Host, o.Port)
        {
            EnableSsl = true,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Credentials = new NetworkCredential(o.User, o.Password),
            Timeout = 20000,
        };
        await client.SendMailAsync(message, ct);
    }
}

/// <summary>Textes des e-mails envoyés par DeeKeep.</summary>
public static class MailTexts
{
    public static (string Subject, string Body) Code(string code) => (
        $"DeeKeep : votre code {code}",
        $"""
        Bonjour,

        Voici votre code DeeKeep : {code}

        Il est valable {AuthCodeStore.Lifetime.TotalMinutes:0} minutes et ne sert qu'une fois. Demander un nouveau
        code annule celui-ci.

        Si vous n'êtes pas à l'origine de cette demande, ignorez ce message.
        """);

    public static (string Subject, string Body) PendingAccount(string email) => (
        $"DeeKeep : nouveau compte à valider ({email})",
        $"""
        Bonjour,

        {email} vient de créer un compte DeeKeep. Il ne pourra s'en servir qu'après votre validation :
        page Administration, bouton « Valider ».
        """);
}
