using System;

namespace BaselineMode.WPF.Core.Models.Shared
{
    public class Result
    {
        public bool IsSuccess { get; }
        public string Error { get; }
        public bool IsFailure => !IsSuccess;

        protected Result(bool isSuccess, string error)
        {
            if (isSuccess && !string.IsNullOrEmpty(error))
                throw new InvalidOperationException();
            if (!isSuccess && string.IsNullOrEmpty(error))
                throw new InvalidOperationException();

            IsSuccess = isSuccess;
            Error = error;
        }

        public static Result Success() => new Result(true, string.Empty);
        public static Result Failure(string error) => new Result(false, error);

        public static Result<T> Success<T>(T value) => new Result<T>(value, true, string.Empty);
        public static Result<T> Failure<T>(string error) => new Result<T>(default, false, error);
    }

    public class Result<T> : Result
    {
        private readonly T _value;
        public T Value
        {
            get
            {
                if (IsFailure)
                    throw new InvalidOperationException("Cannot access value of a failure result.");
                return _value;
            }
        }

        protected internal Result(T? value, bool isSuccess, string error)
            : base(isSuccess, error)
        {
            _value = value!;
        }
    }

    public static class ResultExtensions
    {
        public static Result<T> ToResult<T>(this T value) => Result.Success(value);

        public static Result Bind(this Result result, Func<Result> func)
        {
            if (result.IsFailure) return result;
            return func();
        }

        public static Result<TNext> Bind<T, TNext>(this Result<T> result, Func<T, Result<TNext>> func)
        {
            if (result.IsFailure) return Result.Failure<TNext>(result.Error);
            return func(result.Value);
        }

        public static Result Bind<T>(this Result<T> result, Func<T, Result> func)
        {
            if (result.IsFailure) return Result.Failure(result.Error);
            return func(result.Value);
        }

        public static Result<TNext> Map<T, TNext>(this Result<T> result, Func<T, TNext> func)
        {
            if (result.IsFailure) return Result.Failure<TNext>(result.Error);
            return Result.Success(func(result.Value));
        }

        public static Result OnSuccess(this Result result, Action action)
        {
            if (result.IsSuccess) action();
            return result;
        }

        public static Result<T> OnSuccess<T>(this Result<T> result, Action<T> action)
        {
            if (result.IsSuccess) action(result.Value);
            return result;
        }

        public static Result OnFailure(this Result result, Action<string> action)
        {
            if (result.IsFailure) action(result.Error);
            return result;
        }

        public static Result<T> OnFailure<T>(this Result<T> result, Action<string> action)
        {
            if (result.IsFailure) action(result.Error);
            return result;
        }
    }
}
