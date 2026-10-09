using InternalManagement.Desktop.Services;

namespace InternalManagement.DesktopTests;

public sealed class CscaStudentDialogTests
{
    [Fact]
    public void ResolveAutomaticPaymentStatus_FreeClass_PreservesExplicitPaidSelection()
    {
        Assert.Equal(2, CscaStudentDialog.ResolveAutomaticPaymentStatus(0, 0, 2));
    }

    [Theory]
    [InlineData(1_000_000, 0, 0)]
    [InlineData(1_000_000, 500_000, 1)]
    [InlineData(1_000_000, 1_000_000, 2)]
    public void ResolveAutomaticPaymentStatus_PaidAmount_DeterminesStatus(
        decimal payableAmount, decimal paidAmount, int expectedStatus)
    {
        Assert.Equal(expectedStatus,
            CscaStudentDialog.ResolveAutomaticPaymentStatus(payableAmount, paidAmount, 5));
    }
}
