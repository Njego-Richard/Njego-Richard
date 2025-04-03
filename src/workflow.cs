public enum ApprovalStatus
{
    Draft,
    Submitted,
    InProgress,
    NeedsRevision,
    Approved,
    Rejected,
    Cancelled,
    Resubmitted
}

public enum ConditionType
{
    All,
    Any,
    Specific
}

public class ApprovalRequest
{
    public int Id { get; set; }
    public string Subject { get; set; }
    public string Description { get; set; }
    public ApprovalStatus CurrentStatus { get; set; } = ApprovalStatus.Draft;
    
    public int RequesterId { get; set; }
    public User Requester { get; set; }
    
    public int? PreviousRequestId { get; set; }
    public ApprovalRequest PreviousRequest { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    
    public ICollection<RequestApproval> Approvals { get; set; }
    public ICollection<ApprovalComment> Comments { get; set; }
}

public class ApprovalStep
{
    public int Id { get; set; }
    public string WorkflowType { get; set; }
    public string StepName { get; set; }
    public int StepOrder { get; set; }
    public bool IsParallel { get; set; }
    
    public ICollection<ApprovalDependency> ParentDependencies { get; set; } // Where this is the child
    public ICollection<ApprovalDependency> ChildDependencies { get; set; } // Where this is the parent
    public ICollection<RequestApproval> RequestApprovals { get; set; }
}

public class ApprovalDependency
{
    public int Id { get; set; }
    
    public int ParentStepId { get; set; }
    public ApprovalStep ParentStep { get; set; }
    
    public int ChildStepId { get; set; }
    public ApprovalStep ChildStep { get; set; }
    
    public ConditionType ConditionType { get; set; }
    
    public ICollection<ApprovalCondition> Conditions { get; set; }
}

public class RequestApproval
{
    public int Id { get; set; }
    
    public int RequestId { get; set; }
    public ApprovalRequest Request { get; set; }
    
    public int StepId { get; set; }
    public ApprovalStep Step { get; set; }
    
    public int ApproverId { get; set; }
    public User Approver { get; set; }
    
    public ApprovalStatus Status { get; set; } = ApprovalStatus.Pending;
    public string Comments { get; set; }
    
    public int? DecisiveCommentId { get; set; }
    public ApprovalComment DecisiveComment { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class ApprovalComment
{
    public int Id { get; set; }
    public string CommentText { get; set; }
    public bool IsDecisive { get; set; }
    
    public int RequestId { get; set; }
    public ApprovalRequest Request { get; set; }
    
    public int? ApprovalId { get; set; }
    public RequestApproval Approval { get; set; }
    
    public int UserId { get; set; }
    public User User { get; set; }
    
    public int? ParentCommentId { get; set; }
    public ApprovalComment ParentComment { get; set; }
    
    public ICollection<ApprovalComment> Replies { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class User
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Email { get; set; }
    
    public ICollection<ApprovalRequest> Requests { get; set; }
    public ICollection<RequestApproval> Approvals { get; set; }
    public ICollection<ApprovalComment> Comments { get; set; }
}


public class ApprovalWorkflowContext : DbContext
{
    public DbSet<ApprovalRequest> ApprovalRequests { get; set; }
    public DbSet<ApprovalStep> ApprovalSteps { get; set; }
    public DbSet<ApprovalDependency> ApprovalDependencies { get; set; }
    public DbSet<RequestApproval> RequestApprovals { get; set; }
    public DbSet<ApprovalComment> ApprovalComments { get; set; }
    public DbSet<User> Users { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Configure relationships
        modelBuilder.Entity<ApprovalDependency>()
            .HasOne(d => d.ParentStep)
            .WithMany(s => s.ChildDependencies)
            .HasForeignKey(d => d.ParentStepId)
            .OnDelete(DeleteBehavior.Restrict);
            
        modelBuilder.Entity<ApprovalDependency>()
            .HasOne(d => d.ChildStep)
            .WithMany(s => s.ParentDependencies)
            .HasForeignKey(d => d.ChildStepId)
            .OnDelete(DeleteBehavior.Restrict);
            
        // Ensure no circular dependencies
        modelBuilder.Entity<ApprovalDependency>()
            .HasCheckConstraint("CK_Dependency_NoSelfReference", "ParentStepId <> ChildStepId");
            
        // Configure comment hierarchy
        modelBuilder.Entity<ApprovalComment>()
            .HasOne(c => c.ParentComment)
            .WithMany(p => p.Replies)
            .HasForeignKey(c => c.ParentCommentId)
            .OnDelete(DeleteBehavior.Restrict);
            
        // Indexes for performance
        modelBuilder.Entity<ApprovalRequest>()
            .HasIndex(r => r.CurrentStatus);
            
        modelBuilder.Entity<ApprovalStep>()
            .HasIndex(s => new { s.WorkflowType, s.StepOrder });
    }
}

public interface IApprovalWorkflowService
{
    Task<ApprovalRequest> CreateRequestAsync(int requesterId, string subject, string description);
    Task SubmitRequestAsync(int requestId);
    Task<RequestApproval> ApproveStepAsync(int approvalId, int approverId, string comment = null);
    Task<RequestApproval> RejectStepAsync(int approvalId, int approverId, string rejectionReason, bool requiresResubmission);
    Task<ApprovalComment> AddCommentAsync(int requestId, int userId, string comment, int? parentCommentId = null, int? approvalId = null);
    Task<ApprovalRequest> ResubmitRequestAsync(int originalRequestId, int requesterId);
    Task<bool> IsStepReadyForApprovalAsync(int requestId, int stepId);
}

public class ApprovalWorkflowService : IApprovalWorkflowService
{
    private readonly ApprovalWorkflowContext _context;

    public ApprovalWorkflowService(ApprovalWorkflowContext context)
    {
        _context = context;
    }

    public async Task<ApprovalRequest> CreateRequestAsync(int requesterId, string subject, string description)
    {
        var request = new ApprovalRequest
        {
            RequesterId = requesterId,
            Subject = subject,
            Description = description,
            CurrentStatus = ApprovalStatus.Draft
        };

        _context.ApprovalRequests.Add(request);
        await _context.SaveChangesAsync();
        return request;
    }

    public async Task SubmitRequestAsync(int requestId)
    {
        var request = await _context.ApprovalRequests.FindAsync(requestId);
        if (request == null) throw new ArgumentException("Request not found");
        
        request.CurrentStatus = ApprovalStatus.Submitted;
        request.UpdatedAt = DateTime.UtcNow;
        
        // Create initial approval records based on workflow type
        var initialSteps = await _context.ApprovalSteps
            .Where(s => s.WorkflowType == "default" && s.StepOrder == 1)
            .ToListAsync();
            
        foreach (var step in initialSteps)
        {
            _context.RequestApprovals.Add(new RequestApproval
            {
                RequestId = request.Id,
                StepId = step.Id,
                ApproverId = await DetermineApproverAsync(step.Id), // Implement approver assignment logic
                Status = ApprovalStatus.Pending
            });
        }
        
        await _context.SaveChangesAsync();
    }

    public async Task<RequestApproval> ApproveStepAsync(int approvalId, int approverId, string comment = null)
    {
        var approval = await _context.RequestApprovals
            .Include(a => a.Request)
            .FirstOrDefaultAsync(a => a.Id == approvalId);
            
        if (approval == null) throw new ArgumentException("Approval not found");
        if (approval.ApproverId != approverId) throw new UnauthorizedAccessException();
        if (approval.Status != ApprovalStatus.Pending) throw new InvalidOperationException("Approval already processed");
        
        if (!await IsStepReadyForApprovalAsync(approval.RequestId, approval.StepId))
            throw new InvalidOperationException("Step dependencies not satisfied");
        
        approval.Status = ApprovalStatus.Approved;
        approval.UpdatedAt = DateTime.UtcNow;
        
        if (!string.IsNullOrEmpty(comment))
        {
            var approvalComment = new ApprovalComment
            {
                RequestId = approval.RequestId,
                ApprovalId = approval.Id,
                UserId = approverId,
                CommentText = comment,
                IsDecisive = false
            };
            _context.ApprovalComments.Add(approvalComment);
        }
        
        // Check if all approvals for this step are complete
        if (await CheckStepCompletionAsync(approval.RequestId, approval.StepId))
        {
            await ActivateNextStepsAsync(approval.RequestId, approval.StepId);
        }
        
        await _context.SaveChangesAsync();
        return approval;
    }

    public async Task<RequestApproval> RejectStepAsync(int approvalId, int approverId, string rejectionReason, bool requiresResubmission)
    {
        var approval = await _context.RequestApprovals
            .Include(a => a.Request)
            .FirstOrDefaultAsync(a => a.Id == approvalId);
            
        if (approval == null) throw new ArgumentException("Approval not found");
        
        approval.Status = requiresResubmission ? ApprovalStatus.Rejected : ApprovalStatus.NeedsRevision;
        approval.UpdatedAt = DateTime.UtcNow;
        
        var rejectionComment = new ApprovalComment
        {
            RequestId = approval.RequestId,
            ApprovalId = approval.Id,
            UserId = approverId,
            CommentText = rejectionReason,
            IsDecisive = true
        };
        
        _context.ApprovalComments.Add(rejectionComment);
        approval.DecisiveComment = rejectionComment;
        
        approval.Request.CurrentStatus = requiresResubmission ? 
            ApprovalStatus.Rejected : ApprovalStatus.NeedsRevision;
        approval.Request.UpdatedAt = DateTime.UtcNow;
        
        await _context.SaveChangesAsync();
        return approval;
    }

    public async Task<ApprovalComment> AddCommentAsync(int requestId, int userId, string comment, 
        int? parentCommentId = null, int? approvalId = null)
    {
        var approvalComment = new ApprovalComment
        {
            RequestId = requestId,
            ApprovalId = approvalId,
            UserId = userId,
            ParentCommentId = parentCommentId,
            CommentText = comment,
            IsDecisive = false
        };
        
        _context.ApprovalComments.Add(approvalComment);
        await _context.SaveChangesAsync();
        return approvalComment;
    }

    public async Task<ApprovalRequest> ResubmitRequestAsync(int originalRequestId, int requesterId)
    {
        var original = await _context.ApprovalRequests
            .Include(r => r.Comments)
            .FirstOrDefaultAsync(r => r.Id == originalRequestId);
            
        if (original == null) throw new ArgumentException("Original request not found");
        
        var newRequest = new ApprovalRequest
        {
            RequesterId = requesterId,
            Subject = original.Subject,
            Description = original.Description,
            CurrentStatus = ApprovalStatus.Resubmitted,
            PreviousRequestId = original.Id,
            Comments = original.Comments
                .Where(c => c.ParentCommentId != null) // Bring over the comment threads
                .Select(c => new ApprovalComment
                {
                    CommentText = c.CommentText,
                    UserId = c.UserId,
                    ParentCommentId = c.ParentCommentId,
                    IsDecisive = c.IsDecisive,
                    CreatedAt = c.CreatedAt
                }).ToList()
        };
        
        _context.ApprovalRequests.Add(newRequest);
        await _context.SaveChangesAsync();
        return newRequest;
    }

    public async Task<bool> IsStepReadyForApprovalAsync(int requestId, int stepId)
    {
        // Get all dependencies for this step
        var dependencies = await _context.ApprovalDependencies
            .Where(d => d.ChildStepId == stepId)
            .ToListAsync();
            
        if (!dependencies.Any()) return true; // No dependencies
        
        foreach (var dependency in dependencies)
        {
            var parentApprovals = await _context.RequestApprovals
                .Where(a => a.RequestId == requestId && a.StepId == dependency.ParentStepId)
                .ToListAsync();
                
            switch (dependency.ConditionType)
            {
                case ConditionType.All:
                    if (parentApprovals.Any(a => a.Status != ApprovalStatus.Approved))
                        return false;
                    break;
                    
                case ConditionType.Any:
                    if (!parentApprovals.Any(a => a.Status == ApprovalStatus.Approved))
                        return false;
                    break;
                    
                case ConditionType.Specific:
                    // Implement specific condition checks
                    throw new NotImplementedException();
            }
        }
        
        return true;
    }

    private async Task<bool> CheckStepCompletionAsync(int requestId, int stepId)
    {
        var step = await _context.ApprovalSteps.FindAsync(stepId);
        var approvals = await _context.RequestApprovals
            .Where(a => a.RequestId == requestId && a.StepId == stepId)
            .ToListAsync();
            
        if (step.IsParallel)
        {
            // For parallel steps, we might need just one approval
            return approvals.Any(a => a.Status == ApprovalStatus.Approved);
        }
        else
        {
            // For sequential steps, need all approvals
            return approvals.All(a => a.Status == ApprovalStatus.Approved);
        }
    }

    private async Task ActivateNextStepsAsync(int requestId, int completedStepId)
    {
        var nextSteps = await _context.ApprovalDependencies
            .Where(d => d.ParentStepId == completedStepId)
            .Select(d => d.ChildStep)
            .ToListAsync();
            
        foreach (var step in nextSteps)
        {
            if (await IsStepReadyForApprovalAsync(requestId, step.Id))
            {
                // Create approval records for each approver
                var approvers = await DetermineApproversAsync(step.Id); // Implement approver assignment
                foreach (var approverId in approvers)
                {
                    _context.RequestApprovals.Add(new RequestApproval
                    {
                        RequestId = requestId,
                        StepId = step.Id,
                        ApproverId = approverId,
                        Status = ApprovalStatus.Pending
                    });
                }
            }
        }
        
        await _context.SaveChangesAsync();
    }
}


// Example controller
[ApiController]
[Route("api/approvals")]
public class ApprovalsController : ControllerBase
{
    private readonly IApprovalWorkflowService _service;

    public ApprovalsController(IApprovalWorkflowService service)
    {
        _service = service;
    }

    [HttpPost("requests")]
    public async Task<IActionResult> CreateRequest([FromBody] CreateRequestDto dto)
    {
        var request = await _service.CreateRequestAsync(dto.RequesterId, dto.Subject, dto.Description);
        return CreatedAtAction(nameof(GetRequest), new { id = request.Id }, request);
    }

    [HttpPost("requests/{id}/submit")]
    public async Task<IActionResult> SubmitRequest(int id)
    {
        await _service.SubmitRequestAsync(id);
        return NoContent();
    }

    [HttpPost("approvals/{id}/approve")]
    public async Task<IActionResult> Approve(int id, [FromBody] ApproveDto dto)
    {
        var approval = await _service.ApproveStepAsync(id, dto.ApproverId, dto.Comment);
        return Ok(approval);
    }

    [HttpPost("approvals/{id}/reject")]
    public async Task<IActionResult> Reject(int id, [FromBody] RejectDto dto)
    {
        var approval = await _service.RejectStepAsync(id, dto.ApproverId, dto.Reason, dto.RequiresResubmission);
        return Ok(approval);
    }

    [HttpPost("requests/{id}/comments")]
    public async Task<IActionResult> AddComment(int id, [FromBody] AddCommentDto dto)
    {
        var comment = await _service.AddCommentAsync(id, dto.UserId, dto.Comment, dto.ParentCommentId, dto.ApprovalId);
        return CreatedAtAction(nameof(GetComment), new { id = comment.Id }, comment);
    }

    [HttpPost("requests/{id}/resubmit")]
    public async Task<IActionResult> Resubmit(int id, [FromBody] ResubmitDto dto)
    {
        var request = await _service.ResubmitRequestAsync(id, dto.RequesterId);
        return CreatedAtAction(nameof(GetRequest), new { id = request.Id }, request);
    }
}


public class ApprovalCondition
{
    public int Id { get; set; }
    
    // Links to the dependency this condition belongs to
    public int DependencyId { get; set; }
    public ApprovalDependency Dependency { get; set; }
    
    // Specific approval that must meet the condition
    public int RequiredApprovalId { get; set; }
    public RequestApproval RequiredApproval { get; set; }
    
    // What status the referenced approval must have
    public ApprovalStatus RequiredStatus { get; set; } = ApprovalStatus.Approved;
    
    // Additional condition parameters if needed
    public string Parameter { get; set; }
}

public DbSet<ApprovalCondition> ApprovalConditions { get; set; }

protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    // ... existing configurations ...
    
    modelBuilder.Entity<ApprovalCondition>()
        .HasOne(ac => ac.Dependency)
        .WithMany(d => d.Conditions)
        .HasForeignKey(ac => ac.DependencyId);
        
    modelBuilder.Entity<ApprovalCondition>()
        .HasOne(ac => ac.RequiredApproval)
        .WithMany()
        .HasForeignKey(ac => ac.RequiredApprovalId)
        .OnDelete(DeleteBehavior.Restrict);
}


public async Task<bool> IsStepReadyForApprovalAsync(int requestId, int stepId)
{
    var dependencies = await _context.ApprovalDependencies
        .Include(d => d.Conditions)
        .Where(d => d.ChildStepId == stepId)
        .ToListAsync();

    foreach (var dependency in dependencies)
    {
        switch (dependency.ConditionType)
        {
            case ConditionType.Specific:
                var unmetConditions = await _context.ApprovalConditions
                    .Where(c => c.DependencyId == dependency.Id)
                    .AnyAsync(c => 
                        c.RequiredApproval.Status != c.RequiredStatus ||
                        (c.Parameter != null && !CheckParameterCondition(c)));
                
                if (unmetConditions) return false;
                break;
                
            // ... handle other condition types ...
        }
    }
    return true;
}



// Create the conditions
var condition1 = new ApprovalCondition 
{
    RequiredApprovalId = johnApprovalId,
    RequiredStatus = ApprovalStatus.Approved
};

var condition2 = new ApprovalCondition 
{
    RequiredApprovalId = financeApprovalId,
    RequiredStatus = ApprovalStatus.Approved,
    Parameter = "Amount>5000" // Custom parameter logic
};

var condition3 = new ApprovalCondition 
{
    RequiredApprovalId = vpOverrideApprovalId,
    RequiredStatus = ApprovalStatus.Approved
};

// Add to dependency
var dependency = new ApprovalDependency
{
    ParentStepId = 1,
    ChildStepId = 3,
    ConditionType = ConditionType.Specific,
    Conditions = new List<ApprovalCondition> { condition1, condition2, condition3 }
};

public enum ConditionType
{
    All,        // All parent steps must be approved
    Any,        // Any one parent step must be approved
    Specific    // Custom conditions must be met
}

public class ApprovalDependency
{
    public int Id { get; set; }
    
    public int ParentStepId { get; set; }
    public ApprovalStep ParentStep { get; set; }
    
    public int ChildStepId { get; set; }
    public ApprovalStep ChildStep { get; set; }
    
    public ConditionType ConditionType { get; set; }
    
    public ICollection<ApprovalCondition> Conditions { get; set; }
}

public class ApprovalCondition
{
    public int Id { get; set; }
    
    public int DependencyId { get; set; }
    public ApprovalDependency Dependency { get; set; }
    
    public int RequiredApprovalId { get; set; }
    public RequestApproval RequiredApproval { get; set; }
    
    public ApprovalStatus RequiredStatus { get; set; } = ApprovalStatus.Approved;
    
    public string Parameter { get; set; } // For custom conditions
}

protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    // Configure the self-referencing dependency relationship
    modelBuilder.Entity<ApprovalDependency>()
        .HasOne(d => d.ParentStep)
        .WithMany(s => s.ChildDependencies)
        .HasForeignKey(d => d.ParentStepId)
        .OnDelete(DeleteBehavior.Restrict);
        
    modelBuilder.Entity<ApprovalDependency>()
        .HasOne(d => d.ChildStep)
        .WithMany(s => s.ParentDependencies)
        .HasForeignKey(d => d.ChildStepId)
        .OnDelete(DeleteBehavior.Restrict);
        
    // Prevent circular dependencies
    modelBuilder.Entity<ApprovalDependency>()
        .HasCheckConstraint("CK_Dependency_NoSelfReference", "ParentStepId <> ChildStepId");
        
    // Configure conditions
    modelBuilder.Entity<ApprovalCondition>()
        .HasOne(c => c.Dependency)
        .WithMany(d => d.Conditions)
        .HasForeignKey(c => c.DependencyId);
}


public class DependencyService
{
    private readonly ApprovalWorkflowContext _context;

    public DependencyService(ApprovalWorkflowContext context)
    {
        _context = context;
    }

    // Create a basic dependency
    public async Task<ApprovalDependency> CreateDependencyAsync(
        int parentStepId, 
        int childStepId, 
        ConditionType conditionType)
    {
        var dependency = new ApprovalDependency
        {
            ParentStepId = parentStepId,
            ChildStepId = childStepId,
            ConditionType = conditionType
        };

        _context.ApprovalDependencies.Add(dependency);
        await _context.SaveChangesAsync();
        return dependency;
    }

    // Add a specific condition to a dependency
    public async Task<ApprovalCondition> AddConditionAsync(
        int dependencyId,
        int requiredApprovalId,
        ApprovalStatus requiredStatus = ApprovalStatus.Approved,
        string parameter = null)
    {
        var condition = new ApprovalCondition
        {
            DependencyId = dependencyId,
            RequiredApprovalId = requiredApprovalId,
            RequiredStatus = requiredStatus,
            Parameter = parameter
        };

        _context.ApprovalConditions.Add(condition);
        await _context.SaveChangesAsync();
        return condition;
    }

    // Create a complete workflow structure
    public async Task CreateWorkflowStructureAsync(string workflowType)
    {
        // Example: Create a three-step workflow with dependencies
        var steps = new[]
        {
            new ApprovalStep { WorkflowType = workflowType, StepName = "Initial Review", StepOrder = 1 },
            new ApprovalStep { WorkflowType = workflowType, StepName = "Department Approval", StepOrder = 2 },
            new ApprovalStep { WorkflowType = workflowType, StepName = "Final Sign-off", StepOrder = 3 }
        };

        await _context.ApprovalSteps.AddRangeAsync(steps);
        await _context.SaveChangesAsync();

        // Create dependencies between them
        await CreateDependencyAsync(steps[0].Id, steps[1].Id, ConditionType.All);
        await CreateDependencyAsync(steps[1].Id, steps[2].Id, ConditionType.All);
    }
}

var service = new DependencyService(context);

// Create steps
var step1 = new ApprovalStep { WorkflowType = "purchase", StepName = "Manager", StepOrder = 1 };
var step2 = new ApprovalStep { WorkflowType = "purchase", StepName = "Director", StepOrder = 2 };

await context.ApprovalSteps.AddRangeAsync(step1, step2);
await context.SaveChangesAsync();

// Create dependency
await service.CreateDependencyAsync(step1.Id, step2.Id, ConditionType.All);



// Create steps
var steps = new[]
{
    new ApprovalStep { WorkflowType = "contract", StepName = "Legal", StepOrder = 1 },
    new ApprovalStep { WorkflowType = "contract", StepName = "Finance", StepOrder = 2 },
    new ApprovalStep { WorkflowType = "contract", StepName = "Execution", StepOrder = 3 }
};

await context.ApprovalSteps.AddRangeAsync(steps);
await context.SaveChangesAsync();

// Create dependency with specific conditions
var dependency = await service.CreateDependencyAsync(
    parentStepId: steps[1].Id, 
    childStepId: steps[2].Id, 
    conditionType: ConditionType.Specific);

// Add specific conditions
await service.AddConditionAsync(dependency.Id, financeApprovalId, ApprovalStatus.Approved);
await service.AddConditionAsync(dependency.Id, vpOverrideId, ApprovalStatus.Approved, "Amount>10000");

// Create steps
var steps = new[]
{
    new ApprovalStep { WorkflowType = "contract", StepName = "Legal", StepOrder = 1 },
    new ApprovalStep { WorkflowType = "contract", StepName = "Finance", StepOrder = 2 },
    new ApprovalStep { WorkflowType = "contract", StepName = "Execution", StepOrder = 3 }
};

await context.ApprovalSteps.AddRangeAsync(steps);
await context.SaveChangesAsync();

// Create dependency with specific conditions
var dependency = await service.CreateDependencyAsync(
    parentStepId: steps[1].Id, 
    childStepId: steps[2].Id, 
    conditionType: ConditionType.Specific);

// Add specific conditions
await service.AddConditionAsync(dependency.Id, financeApprovalId, ApprovalStatus.Approved);
await service.AddConditionAsync(dependency.Id, vpOverrideId, ApprovalStatus.Approved, "Amount>10000");


// Create parallel approval steps
var parallelSteps = new[]
{
    new ApprovalStep { WorkflowType = "hr", StepName = "HR Review", StepOrder = 2, IsParallel = true },
    new ApprovalStep { WorkflowType = "hr", StepName = "Compliance", StepOrder = 2, IsParallel = true }
};

await context.ApprovalSteps.AddRangeAsync(parallelSteps);
await context.SaveChangesAsync();

// Both parallel steps depend on initial approval
var initialStep = await context.ApprovalSteps
    .FirstAsync(s => s.WorkflowType == "hr" && s.StepOrder == 1);

await service.CreateDependencyAsync(initialStep.Id, parallelSteps[0].Id, ConditionType.Any);
await service.CreateDependencyAsync(initialStep.Id, parallelSteps[1].Id, ConditionType.Any);


public async Task<bool> AreDependenciesSatisfied(int requestId, int stepId)
{
    var dependencies = await _context.ApprovalDependencies
        .Include(d => d.Conditions)
        .Where(d => d.ChildStepId == stepId)
        .ToListAsync();

    foreach (var dependency in dependencies)
    {
        switch (dependency.ConditionType)
        {
            case ConditionType.All:
                var parentApprovals = await _context.RequestApprovals
                    .Where(a => a.RequestId == requestId && 
                               a.StepId == dependency.ParentStepId)
                    .ToListAsync();
                
                if (parentApprovals.Any(a => a.Status != ApprovalStatus.Approved))
                    return false;
                break;

            case ConditionType.Any:
                var anyApproved = await _context.RequestApprovals
                    .AnyAsync(a => a.RequestId == requestId && 
                                  a.StepId == dependency.ParentStepId && 
                                  a.Status == ApprovalStatus.Approved);
                if (!anyApproved) return false;
                break;

            case ConditionType.Specific:
                var unmetConditions = await _context.ApprovalConditions
                    .Where(c => c.DependencyId == dependency.Id)
                    .AnyAsync(c => c.RequiredApproval.Status != c.RequiredStatus ||
                                 !CheckParameterCondition(c.Parameter));
                if (unmetConditions) return false;
                break;
        }
    }
    return true;
}




private bool CheckParameterCondition(ApprovalCondition condition)
{
    if (string.IsNullOrEmpty(condition.Parameter))
        return true; // No parameter condition to check
    
    // Get the related approval request
    var request = _context.RequestApprovals
        .Include(a => a.Request)
        .FirstOrDefault(a => a.Id == condition.RequiredApprovalId)?.Request;
    
    if (request == null) return false;

    // Simple example: Check amount conditions
    if (condition.Parameter.StartsWith("Amount>"))
    {
        if (decimal.TryParse(condition.Parameter.Substring(7), out decimal minAmount))
        {
            // Assuming request has an Amount property
            return request.Amount > minAmount; 
        }
    }
    else if (condition.Parameter.StartsWith("Amount<"))
    {
        if (decimal.TryParse(condition.Parameter.Substring(7), out decimal maxAmount))
        {
            return request.Amount < maxAmount;
        }
    }
    
    // Add more parameter checks as needed
    return false; // Default to false if condition isn't met
}



private bool CheckParameterCondition(ApprovalCondition condition)
{
    if (string.IsNullOrEmpty(condition.Parameter))
        return true;

    var request = _context.RequestApprovals
        .Include(a => a.Request)
        .FirstOrDefault(a => a.Id == condition.RequiredApprovalId)?.Request;
    
    if (request == null) return false;

    try
    {
        // Using a simple rule parser
        return EvaluateCondition(condition.Parameter, request);
    }
    catch
    {
        return false;
    }
}

private bool EvaluateCondition(string condition, ApprovalRequest request)
{
    // Example condition format: "Amount > 5000 && Department == 'Finance'"
    // This would require a proper expression evaluator
    
    // Simple implementation for demonstration:
    var parts = condition.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
    
    if (parts.Length == 3) // Simple ternary conditions
    {
        string property = parts[0];
        string op = parts[1];
        string value = parts[2].Trim('\'','"');
        
        switch (property)
        {
            case "Amount":
                decimal amountValue = decimal.Parse(value);
                decimal requestAmount = request.Amount;
                return op switch
                {
                    ">" => requestAmount > amountValue,
                    "<" => requestAmount < amountValue,
                    ">=" => requestAmount >= amountValue,
                    "<=" => requestAmount <= amountValue,
                    "==" => requestAmount == amountValue,
                    _ => false
                };
                
            case "Department":
                return op switch
                {
                    "==" => request.Department == value,
                    "!=" => request.Department != value,
                    _ => false
                };
                
            // Add more property checks as needed
        }
    }
    
    return false;
}

// Using NRules or similar
private readonly IRulesEngine _rulesEngine;

public bool CheckParameterCondition(ApprovalCondition condition)
{
    if (string.IsNullOrEmpty(condition.Parameter))
        return true;

    var request = _context.RequestApprovals
        .Include(a => a.Request)
        .FirstOrDefault(a => a.Id == condition.RequiredApprovalId)?.Request;
    
    if (request == null) return false;

    // Compile the condition into a rule
    var rule = CompileRule(condition.Parameter);
    return _rulesEngine.Evaluate(rule, request);
}


public class ApprovalWorkflowService : IApprovalWorkflowService
{
    private readonly ApprovalWorkflowContext _context;
    
    // ... other methods ...
    
    private bool CheckParameterCondition(ApprovalCondition condition)
    {
        // Implementation here
    }
    
    public async Task<bool> IsStepReadyForApprovalAsync(int requestId, int stepId)
    {
        // Uses CheckParameterCondition internally
        var conditions = await _context.ApprovalConditions
            .Where(c => c.Dependency.ChildStepId == stepId)
            .ToListAsync();
            
        foreach (var condition in conditions)
        {
            if (!CheckParameterCondition(condition))
                return false;
        }
        
        return true;
    }
}


