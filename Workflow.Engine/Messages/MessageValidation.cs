// <copyright file="MessageValidation.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Engine.Messages;

using System;
using LanguageExt;
using LanguageExt.Common;
using static LanguageExt.Prelude;

/// <summary>
/// Provides validation extension methods for workflow messages.
/// Uses LanguageExt Validation for accumulating errors~ ✅
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: These validation methods perform basic checks:
/// - Null checks on required fields
/// - Empty GUID detection
/// - Empty string detection for required strings
/// - Collection null checks (empty is valid, null is not)
/// </para>
/// </remarks>
public static class MessageValidation
{
    #region Supervisor Message Validation

    /// <summary>
    /// Validates a CreateWorkflowInstance message.
    /// </summary>
    /// <param name="message">The message to validate.</param>
    /// <returns>Validation result with the message or accumulated errors.</returns>
    public static Validation<Error, CreateWorkflowInstance> Validate(this CreateWorkflowInstance message)
    {
        var validations = Seq<Validation<Error, Unit>>();

        if (message.WorkflowId == Guid.Empty)
        {
            validations = validations.Add(Fail<Error, Unit>(Error.New("WorkflowId cannot be empty")));
        }

        if (message.Definition == null)
        {
            validations = validations.Add(Fail<Error, Unit>(Error.New("Definition is required")));
        }

        return validations.Sequence().Match(
            Succ: _ => Success<Error, CreateWorkflowInstance>(message),
            Fail: errors => Fail<Error, CreateWorkflowInstance>(errors));
    }

    /// <summary>
    /// Validates a WorkflowInstanceCreated message.
    /// </summary>
    /// <param name="message">The message to validate.</param>
    /// <returns>Validation result with the message or accumulated errors.</returns>
    public static Validation<Error, WorkflowInstanceCreated> Validate(this WorkflowInstanceCreated message)
    {
        var validations = Seq<Validation<Error, Unit>>();

        if (message.ExecutionId == Guid.Empty)
        {
            validations = validations.Add(Fail<Error, Unit>(Error.New("ExecutionId cannot be empty")));
        }

        if (message.WorkflowId == Guid.Empty)
        {
            validations = validations.Add(Fail<Error, Unit>(Error.New("WorkflowId cannot be empty")));
        }

        return validations.Sequence().Match(
            Succ: _ => Success<Error, WorkflowInstanceCreated>(message),
            Fail: errors => Fail<Error, WorkflowInstanceCreated>(errors));
    }

    /// <summary>
    /// Validates a WorkflowInstanceCreationFailed message.
    /// </summary>
    /// <param name="message">The message to validate.</param>
    /// <returns>Validation result with the message or accumulated errors.</returns>
    public static Validation<Error, WorkflowInstanceCreationFailed> Validate(this WorkflowInstanceCreationFailed message)
    {
        if (message.WorkflowId == Guid.Empty)
        {
            return Fail<Error, WorkflowInstanceCreationFailed>(Error.New("WorkflowId cannot be empty"));
        }

        return Success<Error, WorkflowInstanceCreationFailed>(message);
    }

    /// <summary>
    /// Validates a GetWorkflowStatus message.
    /// </summary>
    /// <param name="message">The message to validate.</param>
    /// <returns>Validation result with the message or accumulated errors.</returns>
    public static Validation<Error, GetWorkflowStatus> Validate(this GetWorkflowStatus message)
    {
        if (message.ExecutionId == Guid.Empty)
        {
            return Fail<Error, GetWorkflowStatus>(Error.New("ExecutionId cannot be empty"));
        }

        return Success<Error, GetWorkflowStatus>(message);
    }

    /// <summary>
    /// Validates a WorkflowStatusResponse message.
    /// </summary>
    /// <param name="message">The message to validate.</param>
    /// <returns>Validation result with the message or accumulated errors.</returns>
    public static Validation<Error, WorkflowStatusResponse> Validate(this WorkflowStatusResponse message)
    {
        if (message.ExecutionId == Guid.Empty)
        {
            return Fail<Error, WorkflowStatusResponse>(Error.New("ExecutionId cannot be empty"));
        }

        return Success<Error, WorkflowStatusResponse>(message);
    }

    #endregion

    #region Executor Message Validation

    /// <summary>
    /// Validates a StartExecution message.
    /// </summary>
    /// <param name="message">The message to validate.</param>
    /// <returns>Validation result with the message or accumulated errors.</returns>
    public static Validation<Error, StartExecution> Validate(this StartExecution message)
    {
        if (message.ExecutionId == Guid.Empty)
        {
            return Fail<Error, StartExecution>(Error.New("ExecutionId cannot be empty"));
        }

        return Success<Error, StartExecution>(message);
    }

    /// <summary>
    /// Validates a CancelExecution message.
    /// </summary>
    /// <param name="message">The message to validate.</param>
    /// <returns>Validation result with the message or accumulated errors.</returns>
    public static Validation<Error, CancelExecution> Validate(this CancelExecution message)
    {
        if (message.ExecutionId == Guid.Empty)
        {
            return Fail<Error, CancelExecution>(Error.New("ExecutionId cannot be empty"));
        }

        return Success<Error, CancelExecution>(message);
    }

    /// <summary>
    /// Validates a PauseExecution message.
    /// </summary>
    /// <param name="message">The message to validate.</param>
    /// <returns>Validation result with the message or accumulated errors.</returns>
    public static Validation<Error, PauseExecution> Validate(this PauseExecution message)
    {
        if (message.ExecutionId == Guid.Empty)
        {
            return Fail<Error, PauseExecution>(Error.New("ExecutionId cannot be empty"));
        }

        return Success<Error, PauseExecution>(message);
    }

    /// <summary>
    /// Validates a ResumeExecution message.
    /// </summary>
    /// <param name="message">The message to validate.</param>
    /// <returns>Validation result with the message or accumulated errors.</returns>
    public static Validation<Error, ResumeExecution> Validate(this ResumeExecution message)
    {
        if (message.ExecutionId == Guid.Empty)
        {
            return Fail<Error, ResumeExecution>(Error.New("ExecutionId cannot be empty"));
        }

        return Success<Error, ResumeExecution>(message);
    }

    /// <summary>
    /// Validates a WorkflowCompleted message.
    /// </summary>
    /// <param name="message">The message to validate.</param>
    /// <returns>Validation result with the message or accumulated errors.</returns>
    public static Validation<Error, WorkflowCompleted> Validate(this WorkflowCompleted message)
    {
        if (message.ExecutionId == Guid.Empty)
        {
            return Fail<Error, WorkflowCompleted>(Error.New("ExecutionId cannot be empty"));
        }

        return Success<Error, WorkflowCompleted>(message);
    }

    /// <summary>
    /// Validates a WorkflowFailed message.
    /// </summary>
    /// <param name="message">The message to validate.</param>
    /// <returns>Validation result with the message or accumulated errors.</returns>
    public static Validation<Error, WorkflowFailed> Validate(this WorkflowFailed message)
    {
        var validations = Seq<Validation<Error, Unit>>();

        if (message.ExecutionId == Guid.Empty)
        {
            validations = validations.Add(Fail<Error, Unit>(Error.New("ExecutionId cannot be empty")));
        }

        if (message.Error == null)
        {
            validations = validations.Add(Fail<Error, Unit>(Error.New("Error is required")));
        }

        return validations.Sequence().Match(
            Succ: _ => Success<Error, WorkflowFailed>(message),
            Fail: errors => Fail<Error, WorkflowFailed>(errors));
    }

    #endregion

    #region Node Message Validation

    /// <summary>
    /// Validates an Execute message.
    /// </summary>
    /// <param name="message">The message to validate.</param>
    /// <returns>Validation result with the message or accumulated errors.</returns>
    public static Validation<Error, Execute> Validate(this Execute message)
    {
        var validations = Seq<Validation<Error, Unit>>();

        if (string.IsNullOrWhiteSpace(message.NodeId))
        {
            validations = validations.Add(Fail<Error, Unit>(Error.New("NodeId cannot be empty")));
        }

        if (message.ExecutionId == Guid.Empty)
        {
            validations = validations.Add(Fail<Error, Unit>(Error.New("ExecutionId cannot be empty")));
        }

        return validations.Sequence().Match(
            Succ: _ => Success<Error, Execute>(message),
            Fail: errors => Fail<Error, Execute>(errors));
    }

    /// <summary>
    /// Validates a NodeExecutionCompleted message.
    /// </summary>
    /// <param name="message">The message to validate.</param>
    /// <returns>Validation result with the message or accumulated errors.</returns>
    public static Validation<Error, NodeExecutionCompleted> Validate(this NodeExecutionCompleted message)
    {
        var validations = Seq<Validation<Error, Unit>>();

        if (string.IsNullOrWhiteSpace(message.NodeId))
        {
            validations = validations.Add(Fail<Error, Unit>(Error.New("NodeId cannot be empty")));
        }

        if (message.ExecutionId == Guid.Empty)
        {
            validations = validations.Add(Fail<Error, Unit>(Error.New("ExecutionId cannot be empty")));
        }

        return validations.Sequence().Match(
            Succ: _ => Success<Error, NodeExecutionCompleted>(message),
            Fail: errors => Fail<Error, NodeExecutionCompleted>(errors));
    }

    /// <summary>
    /// Validates a NodeExecutionFailed message.
    /// </summary>
    /// <param name="message">The message to validate.</param>
    /// <returns>Validation result with the message or accumulated errors.</returns>
    public static Validation<Error, NodeExecutionFailed> Validate(this NodeExecutionFailed message)
    {
        var validations = Seq<Validation<Error, Unit>>();

        if (string.IsNullOrWhiteSpace(message.NodeId))
        {
            validations = validations.Add(Fail<Error, Unit>(Error.New("NodeId cannot be empty")));
        }

        if (message.ExecutionId == Guid.Empty)
        {
            validations = validations.Add(Fail<Error, Unit>(Error.New("ExecutionId cannot be empty")));
        }

        if (message.Error == null)
        {
            validations = validations.Add(Fail<Error, Unit>(Error.New("Error is required")));
        }

        return validations.Sequence().Match(
            Succ: _ => Success<Error, NodeExecutionFailed>(message),
            Fail: errors => Fail<Error, NodeExecutionFailed>(errors));
    }

    /// <summary>
    /// Validates a RetryNode message.
    /// </summary>
    /// <param name="message">The message to validate.</param>
    /// <returns>Validation result with the message or accumulated errors.</returns>
    public static Validation<Error, RetryNode> Validate(this RetryNode message)
    {
        var validations = Seq<Validation<Error, Unit>>();

        if (string.IsNullOrWhiteSpace(message.NodeId))
        {
            validations = validations.Add(Fail<Error, Unit>(Error.New("NodeId cannot be empty")));
        }

        if (message.ExecutionId == Guid.Empty)
        {
            validations = validations.Add(Fail<Error, Unit>(Error.New("ExecutionId cannot be empty")));
        }

        if (message.Attempt < 1)
        {
            validations = validations.Add(Fail<Error, Unit>(Error.New("Attempt must be at least 1")));
        }

        if (message.MaxAttempts < 1)
        {
            validations = validations.Add(Fail<Error, Unit>(Error.New("MaxAttempts must be at least 1")));
        }

        if (message.Attempt > message.MaxAttempts)
        {
            validations = validations.Add(Fail<Error, Unit>(Error.New("Attempt cannot exceed MaxAttempts")));
        }

        return validations.Sequence().Match(
            Succ: _ => Success<Error, RetryNode>(message),
            Fail: errors => Fail<Error, RetryNode>(errors));
    }

    /// <summary>
    /// Validates a NodeRetrying message.
    /// </summary>
    /// <param name="message">The message to validate.</param>
    /// <returns>Validation result with the message or accumulated errors.</returns>
    public static Validation<Error, NodeRetrying> Validate(this NodeRetrying message)
    {
        var validations = Seq<Validation<Error, Unit>>();

        if (string.IsNullOrWhiteSpace(message.NodeId))
        {
            validations = validations.Add(Fail<Error, Unit>(Error.New("NodeId cannot be empty")));
        }

        if (message.ExecutionId == Guid.Empty)
        {
            validations = validations.Add(Fail<Error, Unit>(Error.New("ExecutionId cannot be empty")));
        }

        if (message.Attempt < 1)
        {
            validations = validations.Add(Fail<Error, Unit>(Error.New("Attempt must be at least 1")));
        }

        if (message.LastError == null)
        {
            validations = validations.Add(Fail<Error, Unit>(Error.New("LastError is required")));
        }

        return validations.Sequence().Match(
            Succ: _ => Success<Error, NodeRetrying>(message),
            Fail: errors => Fail<Error, NodeRetrying>(errors));
    }

    #endregion
}

