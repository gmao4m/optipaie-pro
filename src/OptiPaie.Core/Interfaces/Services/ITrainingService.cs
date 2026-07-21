using System.Collections.Generic;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Primitives;

namespace OptiPaie.Core.Interfaces.Services
{
    /// <summary>
    /// Training operations. Owns the session lifecycle and the enrolment rules (one
    /// enrolment per employee per session), resolving participants through the shared
    /// Employees table.
    /// </summary>
    public interface ITrainingService
    {
        Result<long> Save(TrainingSession session);

        Result SetStatus(long sessionId, Core.Enums.TrainingStatus status);

        Result Delete(long sessionId);

        TrainingSession Get(long sessionId);

        TrainingSummary GetSummary(long sessionId);

        /// <summary>Sessions of a company with participant counts.</summary>
        IReadOnlyList<TrainingSummary> GetByCompany(long companyId);

        // -- participants ------------------------------------------------------

        /// <summary>Enrols an employee in a session.</summary>
        Result Enroll(long sessionId, long employeeId);

        /// <summary>Records a participant's outcome (result, score, certificate).</summary>
        Result SetResult(long participantId, Core.Enums.TrainingResult result, string score, string certificateRef);

        /// <summary>Removes a participant from a session.</summary>
        Result RemoveParticipant(long participantId);

        /// <summary>Participants of one session with their names.</summary>
        IReadOnlyList<TrainingParticipantSummary> GetParticipants(long sessionId);

        /// <summary>Training history of one employee.</summary>
        IReadOnlyList<TrainingHistoryItem> GetEmployeeHistory(long employeeId);
    }
}
