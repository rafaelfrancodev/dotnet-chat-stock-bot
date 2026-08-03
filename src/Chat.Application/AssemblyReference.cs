using System.Reflection;

namespace Chat.Application;

/// <summary>Stable handle on this assembly for MediatR/FluentValidation scanning.</summary>
public static class AssemblyReference
{
    public static readonly Assembly Assembly = typeof(AssemblyReference).Assembly;
}
