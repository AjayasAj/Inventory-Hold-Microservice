using InventoryHold.Contracts;
using InventoryHold.Domain.Exceptions;
using InventoryHold.WebApi.ErrorHandling;
using NUnit.Framework;

namespace InventoryHold.UnitTests;

[TestFixture]
public sealed class ErrorMappingTests
{
    private static IEnumerable<TestCaseData> Cases()
    {
        yield return new TestCaseData(new HoldValidationException("bad"), 400, "validation_failed");
        yield return new TestCaseData(new HoldNotFoundException("h1"), 404, "hold_not_found");
        yield return new TestCaseData(new InsufficientStockException("SKU-1", 5, 2), 409, "insufficient_stock");
        yield return new TestCaseData(new HoldNotActiveException("h1", HoldStatus.Released), 409, "hold_not_active");
        yield return new TestCaseData(new HoldExpiredException("h1"), 410, "hold_expired");
        yield return new TestCaseData(new ProductNotFoundException("SKU-X"), 422, "product_not_found");
    }

    [TestCaseSource(nameof(Cases))]
    public void DomainException_MapsToMeaningfulStatusAndStableCode(DomainException exception, int status, string code)
    {
        var problem = DomainExceptionHandler.ToProblem(exception);

        Assert.Multiple(() =>
        {
            Assert.That(problem.Status, Is.EqualTo(status));
            Assert.That(problem.Extensions["code"], Is.EqualTo(code));
            Assert.That(problem.Detail, Is.EqualTo(exception.Message));
        });
    }

    [Test]
    public void InsufficientStock_ProblemCarriesQuantitiesForTheClient()
    {
        var problem = DomainExceptionHandler.ToProblem(new InsufficientStockException("SKU-1", 5, 2));

        Assert.Multiple(() =>
        {
            Assert.That(problem.Extensions["productId"], Is.EqualTo("SKU-1"));
            Assert.That(problem.Extensions["requested"], Is.EqualTo(5));
            Assert.That(problem.Extensions["available"], Is.EqualTo(2));
        });
    }
}
