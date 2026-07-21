using System.Collections.Generic;
using OptiPaie.Core.Entities;

namespace OptiPaie.Core.Interfaces.Repositories
{
    /// <summary>
    /// Persistence for training sessions and their participants. Sessions are
    /// company-scoped; participants reference the shared Employees table.
    /// </summary>
    public interface ITrainingRepository
    {
        TrainingSession GetById(long id);

        /// <summary>Sessions of a company, most recent first.</summary>
        IEnumerable<TrainingSession> GetByCompany(long companyId);

        long Insert(TrainingSession session);

        void Update(TrainingSession session);

        void SoftDelete(long id);

        // -- participants ------------------------------------------------------

        TrainingParticipant GetParticipantById(long id);

        /// <summary>The enrolment of one employee in one session, or null.</summary>
        TrainingParticipant GetParticipant(long sessionId, long employeeId);

        /// <summary>Participants of one session.</summary>
        IEnumerable<TrainingParticipant> GetParticipants(long sessionId);

        /// <summary>Enrolments of one employee across all sessions.</summary>
        IEnumerable<TrainingParticipant> GetByEmployee(long employeeId);

        long InsertParticipant(TrainingParticipant participant);

        void UpdateParticipant(TrainingParticipant participant);

        void DeleteParticipant(long id);
    }
}
