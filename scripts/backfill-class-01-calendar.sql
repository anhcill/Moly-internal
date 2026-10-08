-- ONE-TIME PRODUCTION BACKFILL. Do not run until both Management API and Web LMS
-- deployments support class.schedule.* and class.session.* management events.
-- Run against the Management production PostgreSQL database only.
-- Expected source: class 01 (7b4db65e-897a-4975-aee9-9c487e0c6341),
-- 3 fixed slots, 27 dated sessions, course link internal_595c64b.
-- Safe to rerun: deterministic idempotency keys and source-ID existence checks.
-- This intentionally queues events; the existing outbox dispatcher delivers them.

BEGIN;
SET LOCAL lock_timeout = '5s';
SELECT pg_advisory_xact_lock(hashtext('class-01-calendar-backfill-v1'));

DO $backfill$
DECLARE
    v_class csca_classes%ROWTYPE;
    v_link lms_course_links%ROWTYPE;
    v_schedule csca_class_schedules%ROWTYPE;
    v_session csca_lesson_sessions%ROWTYPE;
    v_now timestamptz := clock_timestamp();
    v_event_id text;
    v_event_type text;
    v_entity_id text;
    v_payload jsonb;
    v_key text;
    v_queued integer := 0;
BEGIN
    SELECT * INTO STRICT v_class FROM csca_classes
    WHERE id = '7b4db65e-897a-4975-aee9-9c487e0c6341'::uuid
      AND code = '01' AND NOT is_deleted;

    SELECT * INTO STRICT v_link FROM lms_course_links
    WHERE course_id = v_class.course_id
      AND company_id = v_class.company_id
      AND source_system = 'CSCA_COURSE_LMS'
      AND external_course_id = 'internal_595c64b';

    IF v_class.start_date IS NULL OR v_class.end_date IS NULL
       OR v_class.start_date::date <> DATE '2026-10-08'
       OR v_class.end_date::date <> DATE '2026-12-08' THEN
        RAISE EXCEPTION 'Class 01 dates changed; review backfill before running';
    END IF;
    IF (SELECT count(*) FROM csca_class_schedules WHERE class_id = v_class.id) <> 3
       OR (SELECT count(*) FROM csca_lesson_sessions WHERE class_id = v_class.id) <> 27 THEN
        RAISE EXCEPTION 'Class 01 schedule/session counts changed; review backfill before running';
    END IF;
    IF EXISTS (SELECT 1 FROM csca_lesson_sessions
               WHERE class_id = v_class.id AND schedule_id IS NULL) THEN
        RAISE EXCEPTION 'A dated session lacks its fixed schedule';
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM integration_outboxes o
        WHERE o.source_system = 'CSCA_COURSE_LMS'
          AND o.aggregate_type = 'LmsCourseLink'
          AND o.aggregate_id = replace(v_link.id::text, '-', '')
          AND o.status = 'Success'
          AND o.payload_json::jsonb ->> 'eventType' = 'class.upserted'
          AND o.payload_json::jsonb -> 'payload' ->> 'classSourceId' = replace(v_class.id::text, '-', '')
    ) THEN
        RAISE EXCEPTION 'Class 01 metadata has not been delivered to Web';
    END IF;

    FOR v_schedule IN SELECT * FROM csca_class_schedules
                      WHERE class_id = v_class.id ORDER BY day_of_week, start_time, id LOOP
        v_entity_id := replace(v_schedule.id::text, '-', '');
        v_event_type := CASE WHEN v_schedule.status = 'Archived'
                             THEN 'class.schedule.archived' ELSE 'class.schedule.upserted' END;
        v_key := 'calendar-backfill-v1:schedule:' || v_entity_id;
        IF EXISTS (
            SELECT 1 FROM integration_outboxes o
            WHERE o.source_system = 'CSCA_COURSE_LMS'
              AND (o.idempotency_key = v_key OR
                   (o.status <> 'Skipped'
                    AND o.payload_json::jsonb ->> 'eventType' IN ('class.schedule.upserted', 'class.schedule.archived')
                    AND o.payload_json::jsonb -> 'payload' ->> 'scheduleSourceId' = v_entity_id))
        ) THEN CONTINUE; END IF;

        v_event_id := replace(gen_random_uuid()::text, '-', '');
        v_payload := jsonb_build_object(
            'classSourceId', replace(v_class.id::text, '-', ''),
            'scheduleSourceId', v_entity_id,
            'lmsScheduleId', v_schedule.external_schedule_id,
            'title', coalesce(nullif(v_schedule.title, ''), v_class.name),
            'dayOfWeek', v_schedule.day_of_week,
            'startTime', to_char(v_schedule.start_time, 'HH24:MI:SS'),
            'endTime', to_char(v_schedule.end_time, 'HH24:MI:SS'),
            'startDate', to_char(coalesce(v_schedule.start_date, v_class.start_date::date), 'YYYY-MM-DD'),
            'endDate', to_char(coalesce(v_schedule.end_date, v_class.end_date::date), 'YYYY-MM-DD'),
            'timezone', coalesce(nullif(v_schedule.timezone, ''), 'Asia/Ho_Chi_Minh'),
            'status', CASE WHEN v_schedule.status = 'Archived' THEN 'archived' ELSE 'active' END,
            'meetingUrl', v_schedule.meeting_url,
            'sourceUpdatedAt', v_now);
        INSERT INTO integration_outboxes
            (id, company_id, business_unit_id, source_system, event_id, event_type,
             aggregate_type, aggregate_id, idempotency_key, correlation_id, payload_json,
             status, attempt_count, next_attempt_at, created_at, created_by)
        VALUES
            (gen_random_uuid(), v_link.company_id, v_link.business_unit_id,
             'CSCA_COURSE_LMS', v_event_id, 'lms.management.event.requested',
             'LmsCourseLink', replace(v_link.id::text, '-', ''), v_key,
             'class-01-calendar-backfill-v1',
             jsonb_build_object('eventId', v_event_id, 'eventType', v_event_type,
               'occurredAt', v_now, 'source', 'internal-management', 'payload', v_payload)::text,
             'Pending', 0, v_now, v_now + (v_queued * interval '1 millisecond'),
             'calendar-backfill-v1')
        ON CONFLICT (source_system, idempotency_key) DO NOTHING;
        v_queued := v_queued + 1;
    END LOOP;

    FOR v_session IN SELECT * FROM csca_lesson_sessions
                     WHERE class_id = v_class.id ORDER BY lesson_date, start_time, id LOOP
        v_entity_id := replace(v_session.id::text, '-', '');
        v_event_type := CASE WHEN v_session.status = 'Cancelled'
                             THEN 'class.session.cancelled' ELSE 'class.session.upserted' END;
        v_key := 'calendar-backfill-v1:session:' || v_entity_id;
        IF EXISTS (
            SELECT 1 FROM integration_outboxes o
            WHERE o.source_system = 'CSCA_COURSE_LMS'
              AND (o.idempotency_key = v_key OR
                   (o.status <> 'Skipped'
                    AND o.payload_json::jsonb ->> 'eventType' IN ('class.session.upserted', 'class.session.cancelled')
                    AND o.payload_json::jsonb -> 'payload' ->> 'sessionSourceId' = v_entity_id))
        ) THEN CONTINUE; END IF;

        v_event_id := replace(gen_random_uuid()::text, '-', '');
        v_payload := jsonb_build_object(
            'classSourceId', replace(v_class.id::text, '-', ''),
            'sessionSourceId', v_entity_id,
            'lmsSessionId', v_session.external_session_id,
            'scheduleSourceId', replace(v_session.schedule_id::text, '-', ''),
            'lessonDate', to_char(v_session.lesson_date, 'YYYY-MM-DD'),
            'startTime', to_char(v_session.start_time, 'HH24:MI:SS'),
            'endTime', to_char(v_session.end_time, 'HH24:MI:SS'),
            'timezone', 'Asia/Ho_Chi_Minh',
            'status', CASE WHEN v_session.status = 'Completed' THEN 'ended'
                           ELSE lower(v_session.status) END,
            'meetingUrl', v_session.meeting_url,
            'sourceUpdatedAt', v_now);
        INSERT INTO integration_outboxes
            (id, company_id, business_unit_id, source_system, event_id, event_type,
             aggregate_type, aggregate_id, idempotency_key, correlation_id, payload_json,
             status, attempt_count, next_attempt_at, created_at, created_by)
        VALUES
            (gen_random_uuid(), v_link.company_id, v_link.business_unit_id,
             'CSCA_COURSE_LMS', v_event_id, 'lms.management.event.requested',
             'LmsCourseLink', replace(v_link.id::text, '-', ''), v_key,
             'class-01-calendar-backfill-v1',
             jsonb_build_object('eventId', v_event_id, 'eventType', v_event_type,
               'occurredAt', v_now, 'source', 'internal-management', 'payload', v_payload)::text,
             'Pending', 0, v_now, v_now + (v_queued * interval '1 millisecond'),
             'calendar-backfill-v1')
        ON CONFLICT (source_system, idempotency_key) DO NOTHING;
        v_queued := v_queued + 1;
    END LOOP;

    RAISE NOTICE 'Class 01 calendar backfill considered % rows; check inserted count and delivery status after commit', v_queued;
END;
$backfill$;

COMMIT;
