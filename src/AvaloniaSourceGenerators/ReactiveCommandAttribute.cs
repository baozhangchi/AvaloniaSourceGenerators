using System;

namespace AvaloniaSourceGenerators;

[AttributeUsage(AttributeTargets.Method)]
public class ReactiveCommandAttribute : Attribute
{
    public string? CanExecute { get; set; }
}