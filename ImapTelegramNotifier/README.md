# Template Processor

A powerful and flexible C# template processing library that enables dynamic string interpolation with expression evaluation, function support, and contextual replacements.

## Overview

TemplateProcessor is a C# library that allows you to create templates with placeholders and evaluate them at runtime using dynamic context objects. This library supports:

- Simple property placeholders (`{{Property}}`)
- Nested property navigation (`{{User.Profile.Name}}`)
- Built-in functions for string manipulation and conditional logic
- Support for static properties like `DateTime.Now`
- Custom escape functions for output sanitization
- Special replacements for complex substitutions

## Installation

Add the ImapTelegramNotifier namespace to your project to use the TemplateProcessor.

## Usage

### Basic Example

```csharp
using ImapTelegramNotifier;

// A simple class to use as context
class User 
{
    public string Name { get; set; }
    public int Age { get; set; }
}

// Create a context object
var user = new User { Name = "John", Age = 30 };

// Define a template with placeholders
string template = "Hello {{Name}}, you are {{Age}} years old.";

// Process the template with the context object
string result = TemplateProcessor.EvaluateTemplate(template, user);
// Output: "Hello John, you are 30 years old."
```

### Using Functions

The TemplateProcessor supports several built-in functions:

```csharp
// Template with functions
string template = "Welcome {{UPPER(Name)}}! Today is {{FORMAT(DateTime.Now, 'yyyy-MM-dd')}}";

// Process the template
string result = TemplateProcessor.EvaluateTemplate(template, user);
// Output: "Welcome JOHN! Today is 2025-05-13"
```

### Conditional Logic

```csharp
string template = "Hello {{Name}}, you are {{IF(Age > 18, 'an adult', 'a minor')}}.";

string result = TemplateProcessor.EvaluateTemplate(template, user);
// Output: "Hello John, you are an adult."
```

### Known Replacements

You can define special replacements for complex substitutions:

```csharp
var knownReplacements = new Dictionary<string, Func<string, string>>
{
    ["[[CURRENT_TIME]]"] = _ => DateTime.Now.ToString("HH:mm:ss")
};

string template = "The current time is [[CURRENT_TIME]]";
string result = TemplateProcessor.EvaluateTemplate(template, null, knownReplacements);
// Output: "The current time is 14:30:45" (depending on the current time)
```

### Escape Functions

You can provide functions to escape or sanitize the output:

```csharp
var escapeFunctions = new Dictionary<string, Func<string?, string?>>
{
    ["*"] = value => System.Web.HttpUtility.HtmlEncode(value), // Apply to all placeholders
    ["RawHtml"] = value => value // Don't escape this specific placeholder
};

string template = "Welcome {{Name}}! {{RawHtml}}";
string result = TemplateProcessor.EvaluateTemplate(template, user, null, escapeFunctions);
// Output with HTML encoding for Name but not for RawHtml
```

## Supported Functions

### String Functions

- `UPPER(text)`: Converts text to uppercase
- `LOWER(text)`: Converts text to lowercase
- `SUBSTRING(text, startIndex, length)`: Extracts a substring
- `CONCAT(value1, value2, ...)`: Concatenates multiple values

### Conditional Functions

- `IF(condition, trueValue, falseValue)`: Returns one of two values based on a condition
- `EQUALS(value1, value2)`: Checks if two values are equal
- `CONTAINS(text, searchText)`: Checks if text contains a substring

### Formatting Functions

- `FORMAT(value, format)`: Formats a value using the specified format string

## Special Expressions

These expressions are automatically handled:

- `DateTime.Now`: Current local date and time
- `DateTime.Today`: Current local date (time set to 00:00:00)
- `DateTime.UtcNow`: Current UTC date and time

## Handling Literals

The processor supports literal values directly in templates:

- String literals: `'text'` or `"text"`
- Numeric literals: integers, decimals, floats, doubles

## Notes

- Placeholder format: `{{expression}}`
- Function format: `{{functionName(arg1, arg2, ...)}}`
- Nested functions are supported: `{{UPPER(SUBSTRING(Name, 0, 1))}}`
- Static properties can be accessed with full notation: `{{DateTime.Now}}`

## Error Handling

- If a placeholder cannot be evaluated, it is left as is in the result
- Function calls with invalid arguments will throw appropriate exceptions

## Performance Considerations

- Regular expressions are compiled for better performance
- StringBuilder is used for efficient string manipulation
- Evaluation results are cached to avoid redundant processing
