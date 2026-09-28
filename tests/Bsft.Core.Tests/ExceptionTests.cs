using FluentAssertions;
using Xunit;

namespace Bsft.Core.Tests;

public class ExceptionTests
{
    [Fact]
    public void BsftException_StoresMessageAndErrorCode()
    {
        var ex = new BsftException("Something went wrong", "ERR001");

        ex.Message.Should().Be("Something went wrong");
        ex.ErrorCode.Should().Be("ERR001");
    }

    [Fact]
    public void BsftException_WithInnerException_StoresBothExceptions()
    {
        var inner = new InvalidOperationException("inner");
        var ex = new BsftException("outer", inner, "ERR002");

        ex.Message.Should().Be("outer");
        ex.InnerException.Should().BeSameAs(inner);
        ex.ErrorCode.Should().Be("ERR002");
    }

    [Fact]
    public void BsftException_WithNullErrorCode_HasNullErrorCode()
    {
        var ex = new BsftException("message");

        ex.ErrorCode.Should().BeNull();
    }

    [Fact]
    public void BsftException_WithNoArguments_CarriesTheRuntimeMessage()
    {
        var ex = new BsftException();

        ex.ErrorCode.Should().BeNull();
        ex.Message.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void BsftException_WithMessageAndInnerException_KeepsTheCauseWithoutAnErrorCode()
    {
        var inner = new InvalidOperationException("inner");
        var ex = new BsftException("outer", inner);

        ex.Message.Should().Be("outer");
        ex.InnerException.Should().BeSameAs(inner);
        ex.ErrorCode.Should().BeNull();
    }

    [Fact]
    public void ValidationException_StoresErrors()
    {
        var errors = new Dictionary<string, string[]>
        {
            ["Name"] = ["Required", "Too short"],
            ["Email"] = ["Invalid format"]
        };

        var ex = new ValidationException(errors);

        ex.Errors.Should().HaveCount(2);
        ex.Errors["Name"].Should().HaveCount(2);
        ex.ErrorCode.Should().Be("VALIDATION_FAILED");
    }

    [Fact]
    public void ValidationException_WithCustomMessage_StoresMessageAndErrors()
    {
        var errors = new Dictionary<string, string[]> { ["Field"] = ["Error"] };
        var ex = new ValidationException("Custom message", errors);

        ex.Message.Should().Be("Custom message");
        ex.Errors.Should().HaveCount(1);
    }

    [Fact]
    public void ValidationException_WithNullErrors_ThrowsArgumentNullException()
    {
        var act = () => new ValidationException(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void NotFoundException_StoresEntityTypeAndId()
    {
        var ex = new NotFoundException("Customer", 42);

        ex.EntityType.Should().Be("Customer");
        ex.EntityId.Should().Be(42);
        ex.ErrorCode.Should().Be("NOT_FOUND");
        ex.Message.Should().Contain("Customer").And.Contain("42");
    }

    [Fact]
    public void NotFoundException_WithAnInnerException_StoresItBesideTheEntityTypeAndId()
    {
        var inner = new InvalidOperationException("reference failed");

        var ex = new NotFoundException("Customer", 42, inner);

        ex.EntityType.Should().Be("Customer");
        ex.EntityId.Should().Be(42);
        ex.ErrorCode.Should().Be("NOT_FOUND");
        ex.Message.Should().Be("Entity 'Customer' with identifier '42' was not found.");
        ex.InnerException.Should().BeSameAs(inner);
    }

    [Fact]
    public void NotFoundException_WithAnInnerExceptionAndANullArgument_ThrowsArgumentNullException()
    {
        var inner = new InvalidOperationException("reference failed");

        var withoutType = () => new NotFoundException(null!, 1, inner);
        var withoutId = () => new NotFoundException("Customer", null!, inner);

        withoutType.Should().Throw<ArgumentNullException>().WithParameterName("entityType");
        withoutId.Should().Throw<ArgumentNullException>().WithParameterName("entityId");
    }

    [Fact]
    public void NotFoundException_WithNullEntityType_ThrowsArgumentNullException()
    {
        var act = () => new NotFoundException(null!, 1);

        act.Should().Throw<ArgumentNullException>().WithParameterName("entityType");
    }

    [Fact]
    public void NotFoundException_WithNullEntityId_ThrowsArgumentNullException()
    {
        var act = () => new NotFoundException("Customer", null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("entityId");
    }

    [Fact]
    public void ForbiddenException_StoresMessage()
    {
        var ex = new ForbiddenException("Access denied");

        ex.Message.Should().Be("Access denied");
        ex.ErrorCode.Should().Be("FORBIDDEN");
    }

    [Fact]
    public void ConflictException_StoresMessage()
    {
        var ex = new ConflictException("Duplicate entry");

        ex.Message.Should().Be("Duplicate entry");
        ex.ErrorCode.Should().Be("CONFLICT");
    }

    [Fact]
    public void BusinessRuleException_StoresMessage()
    {
        var ex = new BusinessRuleException("Order cannot be empty");

        ex.Message.Should().Be("Order cannot be empty");
        ex.ErrorCode.Should().Be("BUSINESS_RULE_VIOLATED");
    }

    [Fact]
    public void BusinessRuleException_WithCustomErrorCode_StoresCustomCode()
    {
        var ex = new BusinessRuleException("Rule violated", "CUSTOM_CODE");

        ex.ErrorCode.Should().Be("CUSTOM_CODE");
    }

    [Fact]
    public void BusinessRuleException_WithoutADeadline_StatesNone()
        => new BusinessRuleException("Rule violated", "CUSTOM_CODE").RetryAfter.Should().BeNull();

    [Fact]
    public void BusinessRuleException_WithADeadline_CarriesTheInstantTheRefusalLifts()
    {
        // A temporary refusal - a lockout, a cooling-off window - reads to a client exactly like a
        // permanent one unless it says when it ends, and the caller that renders "try again at" is the
        // one that caught the exception. Parsing it out of the message would not be carrying it.
        var until = new DateTimeOffset(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);

        var ex = new BusinessRuleException("The account is locked out.", "CREDENTIAL_LOCKED_OUT", until);

        ex.RetryAfter.Should().Be(until);
        ex.ErrorCode.Should().Be("CREDENTIAL_LOCKED_OUT");
        ex.Message.Should().Be("The account is locked out.");
    }

    [Fact]
    public void PreconditionRequiredException_StoresMessageAndErrorCode()
    {
        var ex = new PreconditionRequiredException("If-Match is required");

        ex.Message.Should().Be("If-Match is required");
        ex.ErrorCode.Should().Be("PRECONDITION_REQUIRED");
    }

    [Fact]
    public void PreconditionFailedException_StoresMessageAndErrorCode()
    {
        var ex = new PreconditionFailedException("The resource has changed");

        ex.Message.Should().Be("The resource has changed");
        ex.ErrorCode.Should().Be("PRECONDITION_FAILED");
    }

    [Fact]
    public void ConflictException_WithCustomErrorCode_StoresCustomCode()
    {
        var ex = new ConflictException("The key is in flight", "IDEMPOTENCY_IN_PROGRESS");

        ex.Message.Should().Be("The key is in flight");
        ex.ErrorCode.Should().Be("IDEMPOTENCY_IN_PROGRESS");
    }

    [Fact]
    public void ConflictException_WithAnInnerException_KeepsTheCauseUnderTheGenericCode()
    {
        var cause = new InvalidOperationException("the row was updated by someone else");

        var ex = new ConflictException("The row has moved on", cause);

        ex.Message.Should().Be("The row has moved on");
        ex.ErrorCode.Should().Be("CONFLICT");
        ex.InnerException.Should().BeSameAs(cause);
    }

    [Fact]
    public void ConflictException_WithACustomErrorCodeAndAnInnerException_KeepsBoth()
    {
        var cause = new InvalidOperationException("unique index violated");

        var ex = new ConflictException("The key is in flight", "IDEMPOTENCY_IN_PROGRESS", cause);

        ex.ErrorCode.Should().Be("IDEMPOTENCY_IN_PROGRESS");
        ex.InnerException.Should().BeSameAs(cause);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ConflictException_WithAnInnerExceptionButABlankErrorCode_IsRejected(string? errorCode)
    {
        var construct = () => new ConflictException("msg", errorCode!, new InvalidOperationException());

        construct.Should().Throw<ArgumentException>().WithParameterName("errorCode");
    }

    [Fact]
    public void ForbiddenException_WithAnInnerException_KeepsTheCauseUnderTheGenericCode()
    {
        var cause = new InvalidOperationException("the provider refused the scope");

        var ex = new ForbiddenException("Access denied", cause);

        ex.Message.Should().Be("Access denied");
        ex.ErrorCode.Should().Be("FORBIDDEN");
        ex.InnerException.Should().BeSameAs(cause);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ConflictException_WithBlankErrorCode_IsRejected(string? errorCode)
    {
        var construct = () => new ConflictException("msg", errorCode!);

        construct.Should().Throw<ArgumentException>().WithParameterName("errorCode");
    }

    [Fact]
    public void ConflictException_WithoutANamedResource_CarriesNone()
    {
        var ex = new ConflictException("Duplicate entry");

        ex.ResourceType.Should().BeNull();
        ex.ResourceId.Should().BeNull();
    }

    [Fact]
    public void ConflictException_WithACodeButNoNamedResource_CarriesNone()
    {
        var ex = new ConflictException("The key is in flight", "IDEMPOTENCY_IN_PROGRESS");

        ex.ResourceType.Should().BeNull();
        ex.ResourceId.Should().BeNull();
    }

    [Fact]
    public void ConflictException_WithANamedResource_CarriesWhatWasCollidedWith()
    {
        // A 409 whose only machine-readable content is its code says something collided but not with
        // what, leaving the client to parse the detail prose for the identifier it has to act on.
        var ex = new ConflictException(
            "A legal hold covers it.", "CHAT_COMPLIANCE_HOLD", "legalHold", "hold-7");

        ex.ResourceType.Should().Be("legalHold");
        ex.ResourceId.Should().Be("hold-7");
        ex.ErrorCode.Should().Be("CHAT_COMPLIANCE_HOLD");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ConflictException_WithAResourceButABlankErrorCode_IsRejected(string? errorCode)
    {
        var construct = () => new ConflictException("msg", errorCode!, "legalHold", "hold-7");

        construct.Should().Throw<ArgumentException>().WithParameterName("errorCode");
    }

    [Fact]
    public void ForbiddenException_WithCustomErrorCode_StoresCustomCode()
    {
        var ex = new ForbiddenException("The requester cannot decide it", "WORKFLOW_SELF_APPROVAL");

        ex.Message.Should().Be("The requester cannot decide it");
        ex.ErrorCode.Should().Be("WORKFLOW_SELF_APPROVAL");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ForbiddenException_WithBlankErrorCode_IsRejected(string? errorCode)
    {
        var construct = () => new ForbiddenException("msg", errorCode!);

        construct.Should().Throw<ArgumentException>().WithParameterName("errorCode");
    }

    [Fact]
    public void PreconditionRequiredException_WithCustomErrorCode_StoresCustomCode()
    {
        var ex = new PreconditionRequiredException("A key is required", "IDEMPOTENCY_KEY_REQUIRED");

        ex.Message.Should().Be("A key is required");
        ex.ErrorCode.Should().Be("IDEMPOTENCY_KEY_REQUIRED");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void PreconditionRequiredException_WithBlankErrorCode_IsRejected(string? errorCode)
    {
        var construct = () => new PreconditionRequiredException("msg", errorCode!);

        construct.Should().Throw<ArgumentException>().WithParameterName("errorCode");
    }

    [Fact]
    public void PayloadTooLargeException_StoresMessageAndErrorCode()
    {
        var ex = new PayloadTooLargeException("The body is too large");

        ex.Message.Should().Be("The body is too large");
        ex.ErrorCode.Should().Be("PAYLOAD_TOO_LARGE");
    }

    [Fact]
    public void PayloadTooLargeException_WithCustomErrorCode_StoresCustomCode()
    {
        var ex = new PayloadTooLargeException("Too large to fingerprint", "IDEMPOTENCY_PAYLOAD_TOO_LARGE");

        ex.ErrorCode.Should().Be("IDEMPOTENCY_PAYLOAD_TOO_LARGE");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void PayloadTooLargeException_WithBlankErrorCode_IsRejected(string? errorCode)
    {
        var construct = () => new PayloadTooLargeException("msg", errorCode!);

        construct.Should().Throw<ArgumentException>().WithParameterName("errorCode");
    }

    [Fact]
    public void PaymentRequiredException_StoresMessageAndErrorCode()
    {
        var ex = new PaymentRequiredException(
            "The quota for 'api.calls' is exhausted until 2026-09-01.", "QUOTA_EXCEEDED");

        ex.Message.Should().Be("The quota for 'api.calls' is exhausted until 2026-09-01.");
        ex.ErrorCode.Should().Be("QUOTA_EXCEEDED");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void PaymentRequiredException_WithBlankErrorCode_IsRejected(string? errorCode)
    {
        var construct = () => new PaymentRequiredException("msg", errorCode!);

        construct.Should().Throw<ArgumentException>().WithParameterName("errorCode");
    }

    [Fact]
    public void AllExceptionTypes_InheritFromBsftException()
    {
        new PaymentRequiredException("msg", "QUOTA_EXCEEDED").Should().BeAssignableTo<BsftException>();
        new ValidationException(new Dictionary<string, string[]>()).Should().BeAssignableTo<BsftException>();
        new NotFoundException("Type", 1).Should().BeAssignableTo<BsftException>();
        new ForbiddenException("msg").Should().BeAssignableTo<BsftException>();
        new ConflictException("msg").Should().BeAssignableTo<BsftException>();
        new BusinessRuleException("msg").Should().BeAssignableTo<BsftException>();
        new PreconditionRequiredException("msg").Should().BeAssignableTo<BsftException>();
        new PreconditionFailedException("msg").Should().BeAssignableTo<BsftException>();
        new PayloadTooLargeException("msg").Should().BeAssignableTo<BsftException>();
    }
}
