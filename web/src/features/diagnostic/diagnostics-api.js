import { gql } from '@apollo/client';
import { JOB_FIELDS } from '../../core/jobs/jobs';

export const SERVICES_Q = gql`
  query Services {
    services {
      service
      reachable
      error
      eventsConnected
      info {
        service
        version
        startedAt
        workingSetBytes
        managedHeapBytes
        activeJobs
        outboxPending
        links {
          producer
          connected
          since
          lastError
        }
        tools {
          name
          available
          version
          path
          detail
        }
      }
    }
    settings
  }
`;

export const UPDATE_YTDLP = gql`mutation UpdateYtDlp { updateYtDlp { ${JOB_FIELDS} } }`;
