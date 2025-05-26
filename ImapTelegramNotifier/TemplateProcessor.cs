using Microsoft.Extensions.FileSystemGlobbing.Internal;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace ImapTelegramNotifier
{
    public class TemplateProcessor
    {
        // Compile the regex pattern once for better performance
        private static readonly Regex PlaceholderPattern = new Regex(@"\{\{([^{}]+)\}\}", RegexOptions.Compiled);
        // Pattern to match function calls: functionName(arg1, arg2, ...)
        private static readonly Regex FunctionPattern = new Regex(@"^(\w+)\((.*)\)$", RegexOptions.Compiled);

        public static string EvaluateTemplate(string template, object? contextObject = null, Dictionary<string, Func<string, string>>? knownReplacements = null,
            Dictionary<string, Func<string?, string?>>? escapeFunctions = null )
        {
            if (string.IsNullOrEmpty(template))
                return template;

            if (escapeFunctions == null)
            {
                escapeFunctions = new Dictionary<string, Func<string?, string?>> { };
            }

            // First apply any known replacements (these are special functions that operate on the entire string)
            string result = template;
            if (knownReplacements != null && knownReplacements.Count > 0)
            {
                foreach (var replacement in knownReplacements)
                {
                    string placeholder = replacement.Key;
                    if (result.Contains(placeholder))
                    {
                        var replacementResult = replacement.Value(placeholder);
                        replacementResult = Escape(escapeFunctions, placeholder, replacementResult);
                        result = result.Replace(placeholder, replacementResult);
                    }
                }
            }

            // Now process any remaining placeholders with StringBuilder for better performance
            StringBuilder resultBuilder = new StringBuilder(result);

            // Get all matches
            var matches = PlaceholderPattern.Matches(result);
            // Replacement cache to avoid multiple evaluating
            var replacementCache = new Dictionary<string, object?>();

            // Process matches in reverse order to avoid invalidating indices when replacing
            for (int i = matches.Count - 1; i >= 0; i--)
            {
                Match match = matches[i];
                string fullPlaceholder = match.Value; // The full {{placeholder}}
                string placeholderPath = match.Groups[1].Value.Trim(); // Just the path inside
                int startIndex = match.Index;
                int length = match.Length;

                string? replacementValue = null;

                try
                {
                    object? value = null;
                    if (!replacementCache.TryGetValue(placeholderPath, out var evaluatedObject))
                    {
                        value = EvaluateExpression(placeholderPath, contextObject);
                        replacementCache[placeholderPath] = value;
                    }
                    else
                    {
                        value = evaluatedObject;
                    }

                    if (value != null)
                    {
                        replacementValue = value.ToString();
                        replacementValue = Escape(escapeFunctions, placeholderPath, replacementValue);

                        resultBuilder.Remove(startIndex, length);
                        resultBuilder.Insert(startIndex, replacementValue);
                    }
                }
                catch(ArgumentException)
                {
                    //unknown function evaluating
                    throw;
                }
                catch
                {
                    // Placeholder couldn't be evaluated, leave it as is
                }
            }

            return resultBuilder.ToString();
        }

        private static string? Escape(Dictionary<string, Func<string?, string?>> escapeFunctions, string placeholderPath, string? replacementValue)
        {
            if (escapeFunctions.TryGetValue(placeholderPath, out var escapeFunc))
            {
                replacementValue = escapeFunc(replacementValue);
            }
            else if (escapeFunctions.TryGetValue("*", out var escapeCommonFunc))
            {
                replacementValue = escapeCommonFunc(replacementValue);
            }

            return replacementValue;
        }

        // Helper method to split function arguments, respecting nested functions
        private static List<string> SplitFunctionArguments(string argsString)
        {
            List<string> args = new List<string>();
            if (string.IsNullOrWhiteSpace(argsString))
                return args;

            int depth = 0;
            int lastSplit = 0;

            for (int i = 0; i < argsString.Length; i++)
            {
                char c = argsString[i];

                if (c == '(')
                {
                    depth++;
                }
                else if (c == ')')
                {
                    depth--;
                }
                else if (c == ',' && depth == 0)
                {
                    // Split at this position
                    args.Add(argsString.Substring(lastSplit, i - lastSplit));
                    lastSplit = i + 1;
                }
            }

            // Add the last argument
            args.Add(argsString.Substring(lastSplit));

            return args;
        }

        // Execute a function by name with arguments
        private static object? ExecuteFunction(string functionName, object?[] args)
        {
            switch (functionName.ToUpperInvariant())
            {
                case "IF":
                    // IF(condition, trueValue, falseValue)
                    if (args.Length != 3)
                        throw new ArgumentException("IF function requires 3 arguments: condition, trueValue, falseValue");

                    bool condition = Convert.ToBoolean(args[0]);
                    return condition ? args[1] : args[2];

                case "CONCAT":
                    // CONCAT(value1, value2, ...)
                    if (args.Length < 1)
                        throw new ArgumentException("CONCAT function requires at least 1 argument");

                    StringBuilder sb = new StringBuilder();
                    foreach (var arg in args)
                    {
                        sb.Append(arg?.ToString() ?? string.Empty);
                    }
                    return sb.ToString();

                case "FORMAT":
                    // FORMAT(value, format)
                    if (args.Length != 2)
                        throw new ArgumentException("FORMAT function requires 2 arguments: value, format");
                    if (args[1] == null)
                        throw new ArgumentException("FORMAT function 2 argument (format) must have value");

                    if (args[0] is DateTime dateValue)
                    {
                        return dateValue.ToString(args[1].ToString());
                    }
                    else if (args[0] is IFormattable formattable)
                    {
                        return formattable.ToString(args[1].ToString(), CultureInfo.CurrentCulture);
                    }
                    return args[0]?.ToString() ?? string.Empty;

                case "EQUALS":
                    // EQUALS(value1, value2)
                    if (args.Length != 2)
                        throw new ArgumentException("EQUALS function requires 2 arguments: value1, value2");

                    return Equals(args[0], args[1]);

                case "CONTAINS":
                    // CONTAINS(text, searchText)
                    if (args.Length != 2)
                        throw new ArgumentException("CONTAINS function requires 2 arguments: text, searchText");

                    string text = args[0]?.ToString() ?? string.Empty;
                    string search = args[1]?.ToString() ?? string.Empty;
                    return text.Contains(search);

                case "UPPER":
                    // UPPER(text)
                    if (args.Length != 1)
                        throw new ArgumentException("UPPER function requires 1 argument: text");

                    return (args[0]?.ToString() ?? string.Empty).ToUpper();

                case "LOWER":
                    // LOWER(text)
                    if (args.Length != 1)
                        throw new ArgumentException("LOWER function requires 1 argument: text");

                    return (args[0]?.ToString() ?? string.Empty).ToLower();

                case "SUBSTRING":
                    // SUBSTRING(text, startIndex, length)
                    // SUBSTRING(text, length)
                    if (args.Length != 2 && args.Length != 3)
                        throw new ArgumentException("SUBSTRING function requires 3 arguments: text, startIndex, length or 2 arguments: text, length");

                    string subText = args[0]?.ToString() ?? string.Empty;
                    int startIndex = args.Length == 3 ? Convert.ToInt32(args[1]) : 0;
                    int length = args.Length == 3 ? Convert.ToInt32(args[2]) : Convert.ToInt32(args[1]);

                    if (startIndex < 0 || startIndex >= subText.Length)
                        return string.Empty;

                    if (startIndex + length > subText.Length)
                        length = subText.Length - startIndex;

                    return subText.Substring(startIndex, length);

                case "REGEX":
                    // REGEX(input, pattern, groupIndex)
                    if (args.Length < 2)
                        throw new ArgumentException("REGEX function requires at least 2 arguments: input, pattern, and optionally groupIndex");

                    var input = args[0] as string;
                    var pattern = args[1] as string;
                    var groupIndex = args.Length >= 3 ? Convert.ToInt32(args[2]) : 1;

                    if (input is null)
                        throw new ArgumentException("REGEX: input must be a string");
                    if (pattern is null)
                        throw new ArgumentException("REGEX: pattern must be a string");

                    var match = Regex.Match(input, pattern, RegexOptions.IgnoreCase);
                    if (match.Success && match.Groups.Count > groupIndex)
                        return match.Groups[groupIndex].Value;
                    else
                        return string.Empty;

                default:
                    throw new ArgumentException($"Unknown function: {functionName}");
            }
        }

        // Separate method to evaluate an expression
        private static object? EvaluateExpression(string expression, object? contextObject)
        {
            // Check if the expression is a function call
            Match functionMatch = FunctionPattern.Match(expression);
            if (functionMatch.Success)
            {
                string functionName = functionMatch.Groups[1].Value;
                string argsString = functionMatch.Groups[2].Value;

                // Parse arguments - this is a simple split, but you might need more sophisticated parsing
                // for nested functions or quoted strings
                List<string> args = SplitFunctionArguments(argsString);

                // Evaluate each argument
                var evaluatedArgs = new List<object?>();
                foreach (var arg in args)
                {
                    // Recursively evaluate each argument
                    evaluatedArgs.Add(EvaluateExpression(arg.Trim(), contextObject));
                }

                // Execute the function
                return ExecuteFunction(functionName, evaluatedArgs.ToArray());
            }

            // Check for special expressions first
            if (TryEvaluateSpecialExpression(expression, out object? specialValue))
            {
                return specialValue;
            }

            if (TryEvaluateLiteral(expression, out object? literalValue))
            {
                return literalValue;
            }

            // Split the path into parts (e.g., "user.profile.name" -> ["user", "profile", "name"])
            string[] pathParts = expression.Split('.');

            if (pathParts.Length == 0)
                return null;

            // Start with the first object in the path
            string rootName = pathParts[0];
            object? currentObj = null;
            bool foundRoot = false;

            // First try the provided context object
            if (contextObject != null)
            {
                // If context is a dictionary
                if (contextObject is IDictionary<string, object> contextDict)
                {
                    if (contextDict.TryGetValue(rootName, out currentObj))
                    {
                        foundRoot = true;
                    }
                }
                // If context is a regular object, check its properties
                else
                {
                    var contextProperty = contextObject.GetType().GetProperty(rootName,
                        BindingFlags.Public | BindingFlags.Instance);

                    if (contextProperty != null)
                    {
                        currentObj = contextProperty.GetValue(contextObject);
                        foundRoot = true;
                    }
                }
            }

            // If not found in current instance, try resolving as a static class
            if (!foundRoot)
            {
                Type? staticType = GetTypeByName(rootName);
                if (staticType != null)
                {
                    foundRoot = true;

                    if (pathParts.Length > 1)
                    {
                        // Get the static property
                        var staticProperty = staticType.GetProperty(pathParts[1],
                            BindingFlags.Public | BindingFlags.Static);

                        if (staticProperty != null)
                        {
                            currentObj = staticProperty.GetValue(null);

                            // Start navigation from the 3rd element (index 2)
                            for (int i = 2; i < pathParts.Length && currentObj != null; i++)
                            {
                                currentObj = NavigateProperty(currentObj, pathParts[i]);
                                if (currentObj == null)
                                    break;
                            }

                            return currentObj;
                        }
                    }
                }
            }

            // Navigate through the rest of the path for non-static cases
            if (foundRoot && currentObj != null)
            {
                // Navigate through the rest of the path
                for (int i = 1; i < pathParts.Length && currentObj != null; i++)
                {
                    currentObj = NavigateProperty(currentObj, pathParts[i]);
                    if (currentObj == null)
                        break;
                }
            }

            return currentObj;
        }

        private static bool TryEvaluateLiteral(string expression, out object? staticValue)
        {
            if (string.IsNullOrWhiteSpace(expression))
            {
                staticValue = expression;
                return false;
            }
            var literalValue = expression.Trim();

            if (literalValue.StartsWith('\'') && literalValue.EndsWith('\'') )
            {
                staticValue = expression.Trim('\'');
                return true;
            }
            if (literalValue.StartsWith('\"') && literalValue.EndsWith('\"'))
            {
                staticValue = expression.Trim('\"');
                return true;
            }
            if (int.TryParse(literalValue, out int intValue))
            {
                staticValue = intValue;
                return true;
            }
            if (decimal.TryParse(literalValue, out decimal decimalValue))
            {
                staticValue = decimalValue;
                return true;
            }
            if (float.TryParse(literalValue, out float floatValue))
            {
                staticValue = floatValue;
                return true;
            }
            if (double.TryParse(literalValue, out double doubleValue))
            {
                staticValue = doubleValue;
                return true;
            }
            staticValue = null;
            return false;
        }

        // Helper method to navigate to a property on an object
        private static object? NavigateProperty(object obj, string propertyName)
        {
            // Check if it's a dictionary access
            if (obj is IDictionary<string, object> dict)
            {
                if (dict.TryGetValue(propertyName, out object? dictValue))
                {
                    return dictValue;
                }
            }

            // Regular property access
            PropertyInfo? property = obj.GetType().GetProperty(propertyName,
                BindingFlags.Public | BindingFlags.Instance);

            if (property != null)
            {
                return property.GetValue(obj);
            }

            return null;
        }

        // Helper method to evaluate special expressions like DateTime.Now
        private static bool TryEvaluateSpecialExpression(string expression, out object? value)
        {
            value = null;

            // Handle DateTime.Now
            if (expression.Equals("DateTime.Now", StringComparison.OrdinalIgnoreCase))
            {
                value = DateTime.Now;
                return true;
            }

            // Handle DateTime.Today
            if (expression.Equals("DateTime.Today", StringComparison.OrdinalIgnoreCase))
            {
                value = DateTime.Today;
                return true;
            }

            // Handle DateTime.UtcNow
            if (expression.Equals("DateTime.UtcNow", StringComparison.OrdinalIgnoreCase))
            {
                value = DateTime.UtcNow;
                return true;
            }

            // Add more special expressions as needed

            return false;
        }

        // Helper method to get a Type by its name
        private static Type? GetTypeByName(string typeName)
        {
            // Try common namespaces
            string[] commonNamespaces = new[]
            {
            "", // Current namespace
            "System",
            "System.Collections.Generic",
            "System.Linq",
            "System.IO",
            "System.Text"
            // Add more namespaces as needed
        };

            foreach (var ns in commonNamespaces)
            {
                string fullName = string.IsNullOrEmpty(ns) ? typeName : $"{ns}.{typeName}";
                Type? type = Type.GetType(fullName);
                if (type != null)
                    return type;

                // Try with assembly qualified name for system types
                if (ns == "System")
                {
                    type = Type.GetType($"{fullName}, mscorlib");
                    if (type != null)
                        return type;
                }
            }

            // Search loaded assemblies
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type? type = assembly.GetType(typeName);
                if (type != null)
                    return type;

                type = assembly.GetType($"System.{typeName}");
                if (type != null)
                    return type;
            }

            return null;
        }
    }
}
