namespace ImapTelegramNotifier.Tests
{
    public class TemplateProcessorTests
    {
        private static string EscapeMarkdownV2(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            return text
                .Replace("_", "\\_")
                .Replace("*", "\\*")
                .Replace("[", "\\[")
                .Replace("]", "\\]")
                .Replace("(", "\\(")
                .Replace(")", "\\)")
                .Replace("~", "\\~")
                .Replace("`", "\\`")
                .Replace(">", "\\>")
                .Replace("#", "\\#")
                .Replace("+", "\\+")
                .Replace("-", "\\-")
                .Replace("=", "\\=")
                .Replace("|", "\\|")
                .Replace("{", "\\{")
                .Replace("}", "\\}")
                .Replace(".", "\\.")
                .Replace("!", "\\!");
        }

        private class User
        {
            public string Name { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
            public int ID { get; set; }
            public UserProfile Profile { get; set; } = new UserProfile();
            public bool IsAdmin { get; set; }
        }


        [Fact]
        public void Evaluates_SpecialExpressions_Correctly()
        {
            string template = "Current time: {{DateTime.Now}}\nToday's date: {{DateTime.Today}}";
            string result = TemplateProcessor.EvaluateTemplate(template);

            // Перевіряємо, що дата і час присутні у форматі, схожому на ISO або за замовчуванням
            DateTime parsedDateTime, parsedDate;
            var lines = result.Split('\n');

            Assert.StartsWith("Current time:", lines[0]);
            Assert.StartsWith("Today's date:", lines[1]);

            string timePart = lines[0].Substring("Current time: ".Length).Trim();
            string datePart = lines[1].Substring("Today's date: ".Length).Trim();

            Assert.True(DateTime.TryParse(timePart, out parsedDateTime));
            Assert.True(DateTime.TryParse(datePart, out parsedDate));
        }

        [Fact]
        public void Applies_KnownReplacements_Correctly()
        {
            var replacements = new Dictionary<string, Func<string, string>>
            {
                { "{{special}}", s => s.Replace("{{special}}", "**SPECIAL CONTENT**") }
            };

            string template = "{{DateTime.Now}} - {{special}}";
            string result = TemplateProcessor.EvaluateTemplate(template, null, replacements);

            Assert.Contains("**SPECIAL CONTENT**", result);

            // Перевірка, що дата теж є валідною
            string[] parts = result.Split(" - ");
            Assert.True(DateTime.TryParse(parts[0], out _));
        }

        [Fact]
        public void Applies_GlobalEscapeFunction_Correctly()
        {
            string template = "User: {{user.Name}}, Email: {{user.Email}}";

            var context = new Dictionary<string, object>
            {
                ["user"] = new User
                {
                    Name = "*Bold*",
                    Email = "john_doe@example.com"
                }
            };

            var escapeFunctions = new Dictionary<string, Func<string?, string>>
            {
                { "*", EscapeMarkdownV2 }
            };

            string result = TemplateProcessor.EvaluateTemplate(template, context, null, escapeFunctions);

            Assert.Equal("User: \\*Bold\\*, Email: john\\_doe@example\\.com", result);
        }

        [Fact]
        public void SpecificEscapeFunction_TakesPrecedenceOver_Global()
        {
            string template = "User: {{user.Name}}, Email: {{user.Email}}";

            var context = new Dictionary<string, object>
            {
                ["user"] = new User
                {
                    Name = "*SpecialName*",
                    Email = "john_doe@example.com"
                }
            };

            var escapeFunctions = new Dictionary<string, Func<string?, string>>
            {
                { "user.Name", s => $"`{s}`" },
                { "*", EscapeMarkdownV2 }
            };

            string result = TemplateProcessor.EvaluateTemplate(template, context, null, escapeFunctions);

            Assert.Equal("User: `*SpecialName*`, Email: john\\_doe@example\\.com", result);
        }

        [Fact]
        public void EvaluateTemplate_EmptyTemplate_ReturnsEmptyString()
        {
            // Arrange
            string template = "";

            // Act
            string result = TemplateProcessor.EvaluateTemplate(template);

            // Assert
            Assert.Equal("", result);
        }

        [Fact]
        public void EvaluateTemplate_NullTemplate_ReturnsNull()
        {
            // Arrange
            string? template = null;

            // Act
            string result = TemplateProcessor.EvaluateTemplate(template);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void EvaluateTemplate_NoPlaceholders_ReturnsSameString()
        {
            // Arrange
            string template = "This is a test string with no placeholders";

            // Act
            string result = TemplateProcessor.EvaluateTemplate(template);

            // Assert
            Assert.Equal(template, result);
        }

        [Fact]
        public void EvaluateTemplate_WithContextObject_ReplacesPlaceholders()
        {
            // Arrange
            string template = "Hello, {{User.Name}}!";
            var context = new TestContext { User = new User { Name = "John" } };

            // Act
            string result = TemplateProcessor.EvaluateTemplate(template, context);

            // Assert
            Assert.Equal("Hello, John!", result);
        }

        [Fact]
        public void EvaluateTemplate_WithNestedProperties_ReplacesPlaceholders()
        {
            // Arrange
            string template = "{{User.Profile.Email}} - {{User.Name}}";
            var context = new TestContext
            {
                User = new User
                {
                    Name = "John",
                    Profile = new UserProfile { Email = "john@example.com" }
                }
            };

            // Act
            string result = TemplateProcessor.EvaluateTemplate(template, context);

            // Assert
            Assert.Equal("john@example.com - John", result);
        }

        [Fact]
        public void EvaluateTemplate_WithMultiplePlaceholders_ReplacesAllPlaceholders()
        {
            // Arrange
            string template = "User: {{User.Name}}, Email: {{User.Profile.Email}}, ID: {{User.ID}}";
            var context = new TestContext
            {
                User = new User
                {
                    Name = "John",
                    ID = 123,
                    Profile = new UserProfile { Email = "john@example.com" }
                }
            };

            // Act
            string result = TemplateProcessor.EvaluateTemplate(template, context);

            // Assert
            Assert.Equal("User: John, Email: john@example.com, ID: 123", result);
        }

        [Fact]
        public void EvaluateTemplate_WithNonExistentProperty_LeavesPplaceholderUntouched()
        {
            // Arrange
            string template = "Hello, {{User.NonExistentProperty}}!";
            var context = new TestContext { User = new User { Name = "John" } };

            // Act
            string result = TemplateProcessor.EvaluateTemplate(template, context);

            // Assert
            Assert.Equal("Hello, {{User.NonExistentProperty}}!", result);
        }

        [Fact]
        public void EvaluateTemplate_WithKnownReplacements_AppliesReplacements()
        {
            // Arrange
            string template = "REPLACE_ME";
            var knownReplacements = new Dictionary<string, Func<string, string>>
            {
                { "REPLACE_ME", s => "Replaced!" }
            };

            // Act
            string result = TemplateProcessor.EvaluateTemplate(template, knownReplacements: knownReplacements);

            // Assert
            Assert.Equal("Replaced!", result);
        }

        [Fact]
        public void EvaluateTemplate_WithEscapeFunctions_AppliesEscaping()
        {
            // Arrange
            string template = "Hello, {{User.Name}}!";
            var context = new TestContext { User = new User { Name = "John & Jane" } };
            var escapeFunctions = new Dictionary<string, Func<string?, string>>
            {
                { "User.Name", s => s != null ? s.Replace("&", "&amp;") : string.Empty }
            };

            // Act
            string result = TemplateProcessor.EvaluateTemplate(template, context, escapeFunctions: escapeFunctions);

            // Assert
            Assert.Equal("Hello, John &amp; Jane!", result);
        }

        [Fact]
        public void EvaluateTemplate_WithCommonEscapeFunction_AppliesEscapingToAll()
        {
            // Arrange
            string template = "{{User.Name}} and {{User.Profile.Email}}";
            var context = new TestContext
            {
                User = new User
                {
                    Name = "John & Jane",
                    Profile = new UserProfile { Email = "info@example.com" }
                }
            };
            var escapeFunctions = new Dictionary<string, Func<string?, string>>
            {
                { "*", s => s != null ? s.ToUpper() : string.Empty }
            };

            // Act
            string result = TemplateProcessor.EvaluateTemplate(template, context, escapeFunctions: escapeFunctions);

            // Assert
            Assert.Equal("JOHN & JANE and INFO@EXAMPLE.COM", result);
        }

        [Fact]
        public void EvaluateTemplate_WithDateTime_ReplacesDateTime()
        {
            // Arrange
            string template = "Current date: {{DateTime.Now}}";

            // Act
            string result = TemplateProcessor.EvaluateTemplate(template);

            // Assert
            Assert.StartsWith("Current date: ", result);
            Assert.NotEqual(template, result);
        }

        [Fact]
        public void EvaluateTemplate_WithDateTimeToday_ReplacesDateTimeToday()
        {
            // Arrange
            string template = "Today: {{DateTime.Today}}";

            // Act
            string result = TemplateProcessor.EvaluateTemplate(template);

            // Assert
            Assert.StartsWith("Today: ", result);
            Assert.NotEqual(template, result);
        }

        [Fact]
        public void EvaluateTemplate_WithIfFunction_ReturnsCorrectValue()
        {
            // Arrange
            string template = "{{IF(User.IsAdmin, 'Admin User', 'Regular User')}}";
            var context = new TestContext
            {
                User = new User { IsAdmin = true }
            };

            // Act
            string result = TemplateProcessor.EvaluateTemplate(template, context);

            // Assert
            Assert.Equal("Admin User", result);
        }

        [Fact]
        public void EvaluateTemplate_WithIfFunctionFalse_ReturnsAlternativeValue()
        {
            // Arrange
            string template = "{{IF(User.IsAdmin, 'Admin User', 'Regular User')}}";
            var context = new TestContext
            {
                User = new User { IsAdmin = false }
            };

            // Act
            string result = TemplateProcessor.EvaluateTemplate(template, context);

            // Assert
            Assert.Equal("Regular User", result);
        }

        [Fact]
        public void EvaluateTemplate_WithConcatFunction_ConcatenatesStrings()
        {
            // Arrange
            string template = "{{CONCAT(User.Name, ' - ', User.Profile.Email)}}";
            var context = new TestContext
            {
                User = new User
                {
                    Name = "John",
                    Profile = new UserProfile { Email = "john@example.com" }
                }
            };

            // Act
            string result = TemplateProcessor.EvaluateTemplate(template, context);

            // Assert
            Assert.Equal("John - john@example.com", result);
        }

        [Fact]
        public void EvaluateTemplate_WithFormatFunction_FormatsDateTime()
        {
            // Arrange
            var date = new DateTime(2023, 5, 15);
            var context = new TestContext { TestDate = date };
            string template = "{{FORMAT(TestDate, 'yyyy-MM-dd')}}";

            // Act
            string result = TemplateProcessor.EvaluateTemplate(template, context);

            // Assert
            Assert.Equal("2023-05-15", result);
        }

        [Fact]
        public void EvaluateTemplate_WithEqualsFunction_ReturnsTrueForEqualValues()
        {
            // Arrange
            string template = "{{EQUALS(User.Name, 'John')}}";
            var context = new TestContext
            {
                User = new User { Name = "John" }
            };

            // Act
            string result = TemplateProcessor.EvaluateTemplate(template, context);

            // Assert
            Assert.Equal("True", result);
        }

        [Fact]
        public void EvaluateTemplate_WithEqualsFunction_ReturnsFalseForDifferentValues()
        {
            // Arrange
            string template = "{{EQUALS(User.Name, 'Jane')}}";
            var context = new TestContext
            {
                User = new User { Name = "John" }
            };

            // Act
            string result = TemplateProcessor.EvaluateTemplate(template, context);

            // Assert
            Assert.Equal("False", result);
        }

        [Fact]
        public void EvaluateTemplate_WithContainsFunction_ReturnsTrueWhenContained()
        {
            // Arrange
            string template = "{{CONTAINS(User.Name, 'oh')}}";
            var context = new TestContext
            {
                User = new User { Name = "John" }
            };

            // Act
            string result = TemplateProcessor.EvaluateTemplate(template, context);

            // Assert
            Assert.Equal("True", result);
        }

        [Fact]
        public void EvaluateTemplate_WithContainsFunction_ReturnsFalseWhenNotContained()
        {
            // Arrange
            string template = "{{CONTAINS(User.Name, 'xyz')}}";
            var context = new TestContext
            {
                User = new User { Name = "John" }
            };

            // Act
            string result = TemplateProcessor.EvaluateTemplate(template, context);

            // Assert
            Assert.Equal("False", result);
        }

        [Fact]
        public void EvaluateTemplate_WithUpperFunction_ReturnsUppercaseString()
        {
            // Arrange
            string template = "{{UPPER(User.Name)}}";
            var context = new TestContext
            {
                User = new User { Name = "John" }
            };

            // Act
            string result = TemplateProcessor.EvaluateTemplate(template, context);

            // Assert
            Assert.Equal("JOHN", result);
        }

        [Fact]
        public void EvaluateTemplate_WithLowerFunction_ReturnsLowercaseString()
        {
            // Arrange
            string template = "{{LOWER(User.Name)}}";
            var context = new TestContext
            {
                User = new User { Name = "JOHN" }
            };

            // Act
            string result = TemplateProcessor.EvaluateTemplate(template, context);

            // Assert
            Assert.Equal("john", result);
        }

        [Fact]
        public void EvaluateTemplate_WithSubstringFunction_ReturnsSubstring()
        {
            // Arrange
            string template = "{{SUBSTRING(User.Name, 1, 2)}}";
            var context = new TestContext
            {
                User = new User { Name = "John" }
            };

            // Act
            string result = TemplateProcessor.EvaluateTemplate(template, context);

            // Assert
            Assert.Equal("oh", result);
        }

        [Fact]
        public void EvaluateTemplate_WithNestedFunctions_EvaluatesCorrectly()
        {
            // Arrange
            string template = "{{UPPER(CONCAT(User.Name, ' - ', User.Profile.Email))}}";
            var context = new TestContext
            {
                User = new User
                {
                    Name = "John",
                    Profile = new UserProfile { Email = "john@example.com" }
                }
            };

            // Act
            string result = TemplateProcessor.EvaluateTemplate(template, context);

            // Assert
            Assert.Equal("JOHN - JOHN@EXAMPLE.COM", result);
        }

        [Fact]
        public void ExecuteFunction_WithUnknownFunction_ThrowsException()
        {
            // Arrange
            string template = "{{UNKNOWN_FUNCTION(User.Name)}}";
            var context = new TestContext
            {
                User = new User { Name = "John" }
            };

            // Act & Assert
            var exception = Assert.Throws<ArgumentException>(() =>
                TemplateProcessor.EvaluateTemplate(template, context));
        }

        [Fact]
        public void EvaluateTemplate_WithDictionaryContext_ReplacesPlaceholders()
        {
            // Arrange
            string template = "Hello, {{user}}!";
            var context = new Dictionary<string, object>
            {
                { "user", "John" }
            };

            // Act
            string result = TemplateProcessor.EvaluateTemplate(template, context);

            // Assert
            Assert.Equal("Hello, John!", result);
        }

        [Fact]
        public void EvaluateTemplate_WithNestedDictionaryContext_ReplacesPlaceholders()
        {
            // Arrange
            string template = "{{user.name}} - {{user.email}}";
            var userDict = new Dictionary<string, object>
            {
                { "name", "John" },
                { "email", "john@example.com" }
            };
            var context = new Dictionary<string, object>
            {
                { "user", userDict }
            };

            // Act
            string result = TemplateProcessor.EvaluateTemplate(template, context);

            // Assert
            Assert.Equal("John - john@example.com", result);
        }

        [Fact]
        public void KnownReplacement_WithEscapeFunction_ShouldApplyBoth()
        {
            // Arrange
            string template = "Hello {{name}}! Your security code is: [SECURITY_CODE]";

            var knownReplacements = new Dictionary<string, Func<string, string>>
            {
                { "[SECURITY_CODE]", s => s.Replace("[SECURITY_CODE]", "12345") }
            };

            var escapeFunctions = new Dictionary<string, Func<string?, string?>>
            {
                { "name", s => $"*{s}*" },
                { "*", s => s?.ToUpperInvariant() }
            };

            // Context object with name property
            var context = new { name = "John" };

            // Act
            string result = TemplateProcessor.EvaluateTemplate(
                template,
                context,
                knownReplacements,
                escapeFunctions
            );

            // Assert
            Assert.Equal("Hello *John*! Your security code is: 12345", result);
        }

        [Fact]
        public void Evaluates_RegexExpression_InTemplate_Correctly()
        {
            var context = new { username = "User: Alice" };

            string template = "Extracted name: {{REGEX(username, 'User: (\\w+)', 1)}}";

            string result = TemplateProcessor.EvaluateTemplate(template, context);

            Assert.StartsWith("Extracted name:", result);

            string extracted = result.Substring("Extracted name: ".Length).Trim();

            Assert.Equal("Alice", extracted);
        }

        [Fact]
        public void Evaluates_RegexExpression_WithDefaultGroupIndex()
        {
            string template = "Extracted: {{REGEX('Data=42', 'Data=(\\d+)')}}";

            string result = TemplateProcessor.EvaluateTemplate(template);

            Assert.Equal("Extracted: 42", result);
        }

        // Test helper classes
        private class TestContext
        {
            public User User { get; set; } = new User();
            public DateTime TestDate { get; set; }
        }

        private class UserProfile
        {
            public string Email { get; set; } = string.Empty;
        }
    }
}