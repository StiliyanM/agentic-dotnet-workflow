using System.Diagnostics.CodeAnalysis;

namespace ApmPlayground.Api;

public sealed class JsonRequestBodyResult<T>
    where T : class
{
    internal JsonRequestBodyResult(T value) => Value = value;

    internal JsonRequestBodyResult(IResult error) => Error = error;

    public T? Value { get; }

    public IResult? Error { get; }

    [MemberNotNullWhen(true, nameof(Value))]
    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => Error is null;
}
