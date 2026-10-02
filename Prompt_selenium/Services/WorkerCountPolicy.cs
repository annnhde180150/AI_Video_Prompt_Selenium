using System.IO;

namespace PromptSelenium.Services;

public static class WorkerCountPolicy
{
    public const int Minimum = 1;
    public const int Maximum = 10;
    public const int Default = 10;

    public static void Validate(int workerCount)
    {
        if (workerCount is < Minimum or > Maximum)
        {
            throw new InvalidDataException(
                $"Số worker phải từ {Minimum} đến {Maximum}.");
        }
    }
}
