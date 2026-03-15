#nullable enable

namespace RevitMCPAddin.Core.ResultDelivery
{
    internal interface IResultDeliveryService
    {
        void Enqueue(PendingResultItem item);
        void Start();
        void Stop();
        void SetBaseAddress(string baseAddress);
        void UpdatePort(int port);
    }
}
