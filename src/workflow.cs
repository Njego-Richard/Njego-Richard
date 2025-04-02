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