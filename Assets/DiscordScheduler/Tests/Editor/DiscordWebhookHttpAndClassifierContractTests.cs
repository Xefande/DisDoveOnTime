using System.Net;
using System.Net.Http;
using System.Reflection;
using NUnit.Framework;

namespace DiscordScheduler.Tests
{
    public sealed class DiscordWebhookHttpAndClassifierContractTests
    {
        [Test]
        public void NormalizeWaitTrueUrl_AddsWaitTrue()
        {
            var url = DiscordWebhookHttp11.NormalizeWaitTrueUrl("https://discord.com/api/webhooks/1/token");

            Assert.That(url, Is.EqualTo("https://discord.com/api/webhooks/1/token?wait=true"));
        }

        [Test]
        public void NormalizeWaitTrueUrl_ReplacesExistingWaitAndPreservesFragment()
        {
            var url = DiscordWebhookHttp11.NormalizeWaitTrueUrl(
                "https://discord.com/api/webhooks/1/token?foo=bar&wait=false#debug");

            Assert.That(url, Is.EqualTo("https://discord.com/api/webhooks/1/token?foo=bar&wait=true#debug"));
        }

        [Test]
        public void NormalizeWaitTrueUrl_BlankInput_ReturnsBlank()
        {
            Assert.That(DiscordWebhookHttp11.NormalizeWaitTrueUrl(""), Is.Empty);
            Assert.That(DiscordWebhookHttp11.NormalizeWaitTrueUrl("   "), Is.Empty);
        }

        [Test]
        public void NormalizeWaitTrueUrl_RemovesDuplicateWaitParameters()
        {
            var url = DiscordWebhookHttp11.NormalizeWaitTrueUrl(
                "https://discord.com/api/webhooks/1/token?wait=false&thread_id=123&WAIT=true");

            Assert.That(url, Is.EqualTo("https://discord.com/api/webhooks/1/token?thread_id=123&wait=true"));
        }

        [Test]
        public void RetryAfter_HeaderSeconds_ParsedAndClamped()
        {
            using (var response = new HttpResponseMessage((HttpStatusCode)429))
            {
                response.Headers.TryAddWithoutValidation("Retry-After", "65");

                var retryAfter = GetRetryAfterSeconds(response, "");

                Assert.That(retryAfter, Is.EqualTo(60f));
            }
        }

        [Test]
        public void RetryAfter_BodyFractionalSeconds_ParsedAsSecondsAndClamped()
        {
            using (var response = new HttpResponseMessage((HttpStatusCode)429))
            {
                var retryAfter = GetRetryAfterSeconds(response, "{\"retry_after\":64.57}");

                Assert.That(retryAfter, Is.EqualTo(60f));
            }
        }

        [Test]
        public void RetryAfter_BodySmallFraction_ClampedToMinimum()
        {
            using (var response = new HttpResponseMessage((HttpStatusCode)429))
            {
                var retryAfter = GetRetryAfterSeconds(response, "{\"retry_after\":0.25}");

                Assert.That(retryAfter, Is.EqualTo(0.5f));
            }
        }

        [Test]
        public void RetryAfter_Invalid_DefaultsToTwoSeconds()
        {
            using (var response = new HttpResponseMessage((HttpStatusCode)429))
            {
                var retryAfter = GetRetryAfterSeconds(response, "{\"retry_after\":\"soon\"}");

                Assert.That(retryAfter, Is.EqualTo(2f));
            }
        }

        [Test]
        public void RetryAfter_HeaderSharedScope_MetadataCaptured()
        {
            using (var response = new HttpResponseMessage((HttpStatusCode)429))
            {
                response.Headers.TryAddWithoutValidation("Retry-After", "4.5");
                response.Headers.TryAddWithoutValidation("X-RateLimit-Scope", "shared");

                var info = GetRetryAfterInfo(response, "");

                Assert.That(GetInfoField<float>(info, "seconds"), Is.EqualTo(4.5f));
                Assert.That(GetInfoField<string>(info, "source"), Is.EqualTo(WebhookRetryAfterSource.Header));
                Assert.That(GetInfoField<bool>(info, "global"), Is.False);
                Assert.That(GetInfoField<string>(info, "scope"), Is.EqualTo("shared"));
            }
        }

        [Test]
        public void RetryAfter_BodyGlobal_MetadataCaptured()
        {
            using (var response = new HttpResponseMessage((HttpStatusCode)429))
            {
                var info = GetRetryAfterInfo(response, "{\"retry_after\":9.25,\"global\":true}");

                Assert.That(GetInfoField<float>(info, "seconds"), Is.EqualTo(9.25f));
                Assert.That(GetInfoField<string>(info, "source"), Is.EqualTo(WebhookRetryAfterSource.Body));
                Assert.That(GetInfoField<bool>(info, "global"), Is.True);
                Assert.That(GetInfoField<string>(info, "scope"), Is.EqualTo("global"));
            }
        }

        [Test]
        public void Classifier_TwoOhFourNoBody_IsSuccessWithoutMessageId()
        {
            var result = new WebhookErrorClassifier().Classify(new DiscordWebhookHttp11.Result
            {
                ok = true,
                statusCode = 204,
                body = ""
            });

            Assert.That(result.ok, Is.True);
            Assert.That(result.outcome, Is.EqualTo(SendOutcomeKind.Success));
            Assert.That(result.discordMessageId, Is.Empty);
        }

        [Test]
        public void Classifier_TwoHundredBody_ExtractsDiscordMessageId()
        {
            var result = new WebhookErrorClassifier().Classify(new DiscordWebhookHttp11.Result
            {
                ok = true,
                statusCode = 200,
                body = "{\"id\":\"123456789012345678\",\"content\":\"ok\"}"
            });

            Assert.That(result.ok, Is.True);
            Assert.That(result.outcome, Is.EqualTo(SendOutcomeKind.Success));
            Assert.That(result.discordMessageId, Is.EqualTo("123456789012345678"));
        }

        [Test]
        public void Classifier_RateLimit_UsesRetryAfterOrDefault()
        {
            var classifier = new WebhookErrorClassifier();

            var explicitRetry = classifier.Classify(new DiscordWebhookHttp11.Result
            {
                statusCode = 429,
                body = "{}",
                retryAfterSeconds = 13.5f
            });

            var defaultRetry = classifier.Classify(new DiscordWebhookHttp11.Result
            {
                statusCode = 429,
                body = "{}",
                retryAfterSeconds = 0
            });

            Assert.That(explicitRetry.outcome, Is.EqualTo(SendOutcomeKind.RateLimited));
            Assert.That(explicitRetry.retryAfterSeconds, Is.EqualTo(13.5f));
            Assert.That(defaultRetry.retryAfterSeconds, Is.EqualTo(2f));
        }

        [Test]
        public void Classifier_RateLimit_PreservesGlobalScopeAndRetryAfterSource()
        {
            var result = new WebhookErrorClassifier().Classify(new DiscordWebhookHttp11.Result
            {
                statusCode = 429,
                body = "{\"message\":\"limited\"}",
                retryAfterSeconds = 4.5f,
                retryAfterSource = WebhookRetryAfterSource.Header,
                rateLimitGlobal = true,
                rateLimitScope = "global",
                requestMayHaveReachedDiscord = true
            });

            Assert.That(result.outcome, Is.EqualTo(SendOutcomeKind.RateLimited));
            Assert.That(result.retryAfterSeconds, Is.EqualTo(4.5f));
            Assert.That(result.retryAfterSource, Is.EqualTo(WebhookRetryAfterSource.Header));
            Assert.That(result.rateLimitGlobal, Is.True);
            Assert.That(result.rateLimitScope, Is.EqualTo("global"));
            Assert.That(result.requestMayHaveReachedDiscord, Is.True);
        }

        [Test]
        public void Classifier_StatusZeroNetworkError_IsAmbiguous()
        {
            var result = new WebhookErrorClassifier().Classify(new DiscordWebhookHttp11.Result
            {
                statusCode = 0,
                body = "A task was canceled."
            });

            Assert.That(result.ok, Is.False);
            Assert.That(result.outcome, Is.EqualTo(SendOutcomeKind.Ambiguous));
            Assert.That(result.requestMayHaveReachedDiscord, Is.True);
        }

        [Test]
        public void Classifier_StatusZeroLocalBuild_IsNonRetryableAndDidNotReachDiscord()
        {
            var result = new WebhookErrorClassifier().Classify(new DiscordWebhookHttp11.Result
            {
                statusCode = 0,
                body = "Could not read media file.",
                requestMayHaveReachedDiscord = false,
                errorKind = WebhookTransportErrorKind.LocalRequestBuild
            });

            Assert.That(result.ok, Is.False);
            Assert.That(result.outcome, Is.EqualTo(SendOutcomeKind.NonRetryable));
            Assert.That(result.requestMayHaveReachedDiscord, Is.False);
            Assert.That(result.errorKind, Is.EqualTo(WebhookTransportErrorKind.LocalRequestBuild));
        }

        [Test]
        public void Classifier_StatusZeroMissingWebhook_IsNonRetryable()
        {
            var result = new WebhookErrorClassifier().Classify(new DiscordWebhookHttp11.Result
            {
                statusCode = 0,
                body = "Missing webhook URL."
            });

            Assert.That(result.ok, Is.False);
            Assert.That(result.outcome, Is.EqualTo(SendOutcomeKind.NonRetryable));
        }

        [TestCase(500)]
        [TestCase(502)]
        [TestCase(503)]
        [TestCase(504)]
        public void Classifier_ServerError_IsRetryableTransient(int statusCode)
        {
            var result = new WebhookErrorClassifier().Classify(new DiscordWebhookHttp11.Result
            {
                statusCode = statusCode,
                body = "Service unavailable"
            });

            Assert.That(result.ok, Is.False);
            Assert.That(result.outcome, Is.EqualTo(SendOutcomeKind.RetryableTransient));
        }

        [TestCase(400)]
        [TestCase(401)]
        [TestCase(403)]
        [TestCase(404)]
        [TestCase(499)]
        public void Classifier_ClientError_IsNonRetryable(int statusCode)
        {
            var result = new WebhookErrorClassifier().Classify(new DiscordWebhookHttp11.Result
            {
                statusCode = statusCode,
                body = "Client error"
            });

            Assert.That(result.ok, Is.False);
            Assert.That(result.outcome, Is.EqualTo(SendOutcomeKind.NonRetryable));
            Assert.That(result.shortError, Does.Contain("HTTP " + statusCode));
        }

        [Test]
        public void Classifier_UnexpectedStatus_IsAmbiguous()
        {
            var result = new WebhookErrorClassifier().Classify(new DiscordWebhookHttp11.Result
            {
                statusCode = 102,
                body = "Processing"
            });

            Assert.That(result.ok, Is.False);
            Assert.That(result.outcome, Is.EqualTo(SendOutcomeKind.Ambiguous));
            Assert.That(result.requestMayHaveReachedDiscord, Is.True);
        }

        [Test]
        public void Classifier_ErrorBody_RedactsWebhookTokenAndTruncates()
        {
            var secretUrl = "https://discord.com/api/webhooks/123456789012345678/secret-token";
            var longBody = secretUrl + " " + new string('x', WebhookErrorClassifier.MaxShortErrorLength + 50);

            var result = new WebhookErrorClassifier().Classify(new DiscordWebhookHttp11.Result
            {
                statusCode = 400,
                body = longBody
            });

            Assert.That(result.shortError, Does.Not.Contain("secret-token"));
            Assert.That(result.shortError.Length, Is.LessThanOrEqualTo(WebhookErrorClassifier.MaxShortErrorLength + 3));
            Assert.That(result.shortError, Does.EndWith("..."));
        }

        private static float GetRetryAfterSeconds(HttpResponseMessage response, string body)
        {
            var method = typeof(DiscordWebhookHttp11).GetMethod(
                "GetRetryAfterSeconds",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.That(method, Is.Not.Null);
            return (float)method.Invoke(null, new object[] { response, body });
        }

        private static object GetRetryAfterInfo(HttpResponseMessage response, string body)
        {
            var method = typeof(DiscordWebhookHttp11).GetMethod(
                "GetRetryAfterInfo",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.That(method, Is.Not.Null);
            return method.Invoke(null, new object[] { response, body });
        }

        private static T GetInfoField<T>(object info, string fieldName)
        {
            Assert.That(info, Is.Not.Null);
            var field = info.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
            Assert.That(field, Is.Not.Null);
            return (T)field.GetValue(info);
        }
    }
}
