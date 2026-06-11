namespace HoneyGrid.Sensors.Web;

/// <summary>
/// Statyczne "przynęty" (bait) zwracane atakującym na typowych ścieżkach skanowanych przez boty.
/// Zawartość celowo wygląda wiarygodnie i zawiera honeytokeny (fałszywe, nieaktywne klucze),
/// aby zwiększyć szansę, że atakujący spróbuje ich użyć — co da nam dalszą telemetrię.
/// UWAGA: wszystkie sekrety poniżej są FAŁSZYWE i nieaktywne.
/// </summary>
public static class DecoyContent
{
    /// <summary>Fałszywy panel logowania WordPress (uproszczony, ale wiarygodny HTML).</summary>
    public const string WpLoginHtml =
        """
        <!DOCTYPE html>
        <html lang="en">
        <head>
            <meta charset="utf-8" />
            <title>Log In &lsaquo; My Blog &mdash; WordPress</title>
            <meta name="viewport" content="width=device-width, initial-scale=1.0" />
            <link rel="stylesheet" href="/wp-admin/css/login.min.css?ver=6.5.2" />
        </head>
        <body class="login wp-core-ui">
            <div id="login">
                <h1><a href="https://wordpress.org/">Powered by WordPress</a></h1>
                <form name="loginform" id="loginform" action="/wp-login.php" method="post">
                    <p>
                        <label for="user_login">Username or Email Address</label>
                        <input type="text" name="log" id="user_login" class="input" value="" size="20" />
                    </p>
                    <p>
                        <label for="user_pass">Password</label>
                        <input type="password" name="pwd" id="user_pass" class="input" value="" size="20" />
                    </p>
                    <p class="submit">
                        <input type="submit" name="wp-submit" id="wp-submit" class="button button-primary button-large" value="Log In" />
                    </p>
                </form>
            </div>
        </body>
        </html>
        """;

    /// <summary>Fałszywy plik .env z honeytokenami (nieaktywne wartości).</summary>
    public const string DotEnv =
        """
        APP_NAME=Laravel
        APP_ENV=production
        APP_KEY=base64:hZ8Qd3vK1mP9rT4wX7yB2nC5fG8jL0sV6uN3aE1iO9k=
        APP_DEBUG=false
        APP_URL=http://localhost

        DB_CONNECTION=mysql
        DB_HOST=127.0.0.1
        DB_PORT=3306
        DB_DATABASE=app_prod
        DB_USERNAME=app_user
        DB_PASSWORD=S3cr3t-Pr0d-Db-Pass!

        AWS_ACCESS_KEY_ID=AKIAIOSFODNN7EXAMPLE
        AWS_SECRET_ACCESS_KEY=wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY
        AWS_DEFAULT_REGION=us-east-1

        MAIL_HOST=smtp.mailtrap.io
        MAIL_USERNAME=2a1f9c4b7e8d6a
        MAIL_PASSWORD=9f8e7d6c5b4a3f
        """;

    /// <summary>Fałszywa konfiguracja .git/config.</summary>
    public const string GitConfig =
        """
        [core]
        	repositoryformatversion = 0
        	filemode = true
        	bare = false
        	logallrefupdates = true
        [remote "origin"]
        	url = https://github.com/acme-corp/internal-payments.git
        	fetch = +refs/heads/*:refs/remotes/origin/*
        [branch "main"]
        	remote = origin
        	merge = refs/heads/main
        """;

    /// <summary>Fałszywe poświadczenia AWS (~/.aws/credentials).</summary>
    public const string AwsCredentials =
        """
        [default]
        aws_access_key_id = AKIAIOSFODNN7EXAMPLE
        aws_secret_access_key = wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY

        [deploy]
        aws_access_key_id = AKIAI44QH8DHBEXAMPLE
        aws_secret_access_key = je7MtGbClwBF/2Zp9Utk/h3yCo8nvbEXAMPLEKEY
        """;

    /// <summary>Fałszywy panel logowania phpMyAdmin.</summary>
    public const string PhpMyAdminHtml =
        """
        <!DOCTYPE html>
        <html lang="en">
        <head><meta charset="utf-8" /><title>phpMyAdmin</title></head>
        <body>
            <form method="post" action="index.php" name="login_form">
                <fieldset>
                    <legend>Log in</legend>
                    <label for="input_username">Username:</label>
                    <input type="text" name="pma_username" id="input_username" value="" />
                    <label for="input_password">Password:</label>
                    <input type="password" name="pma_password" id="input_password" value="" />
                    <input type="submit" name="go" value="Go" />
                </fieldset>
            </form>
        </body>
        </html>
        """;

    /// <summary>Fałszywy panel administracyjny (generyczny).</summary>
    public const string AdminHtml =
        """
        <!DOCTYPE html>
        <html lang="en">
        <head><meta charset="utf-8" /><title>Admin Panel</title></head>
        <body>
            <h1>Administration</h1>
            <form method="post" action="/admin">
                <input type="text" name="username" placeholder="Username" />
                <input type="password" name="password" placeholder="Password" />
                <button type="submit">Sign in</button>
            </form>
        </body>
        </html>
        """;

    /// <summary>Fałszywy zrzut Spring Boot Actuator /actuator/env (wycieki konfiguracji).</summary>
    public const string ActuatorEnv =
        """
        {
          "activeProfiles": ["prod"],
          "propertySources": [
            {
              "name": "systemEnvironment",
              "properties": {
                "SPRING_DATASOURCE_URL": { "value": "jdbc:postgresql://db.internal:5432/payments" },
                "SPRING_DATASOURCE_USERNAME": { "value": "svc_payments" },
                "SPRING_DATASOURCE_PASSWORD": { "value": "******" },
                "JWT_SECRET": { "value": "******" }
              }
            }
          ]
        }
        """;

    /// <summary>Fałszywa odpowiedź API root (sugeruje istnienie zasobów).</summary>
    public const string ApiRoot =
        """
        {
          "name": "internal-api",
          "version": "2.3.1",
          "endpoints": ["/api/users", "/api/orders", "/api/admin", "/api/health"],
          "auth": "bearer"
        }
        """;
}
