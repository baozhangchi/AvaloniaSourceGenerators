using System;

namespace Avalonia.InternalCheat;

[AttributeUsage(AttributeTargets.Method)]
public class ReactiveCommandAttribute : Attribute
{
    public string? CanExecute { get; set; }
}