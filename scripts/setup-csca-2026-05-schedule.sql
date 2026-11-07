BEGIN;

DO $$
DECLARE
    v_class_id uuid := '3fe9281d-a8d3-4e30-a3cf-1b3c8c40242d';
    v_sunday_id uuid := gen_random_uuid();
    v_tuesday_id uuid := gen_random_uuid();
    v_thursday_id uuid := gen_random_uuid();
    v_link lms_course_links%ROWTYPE;
    v_now timestamptz := clock_timestamp();
    v_event_id text;
    v_session_id uuid;
    v_meeting_url text;
    v_sequence integer := 0;
    v_created integer := 0;
    v_row record;
BEGIN
    PERFORM pg_advisory_xact_lock(hashtextextended(v_class_id::text, 0));

    IF EXISTS (SELECT 1 FROM csca_class_schedules WHERE class_id = v_class_id)
       OR EXISTS (SELECT 1 FROM csca_lesson_sessions WHERE class_id = v_class_id) THEN
        RAISE EXCEPTION 'Class CSCA-2026-05 already has schedules or sessions; refusing to create duplicates.';
    END IF;

    SELECT link.* INTO STRICT v_link
    FROM lms_course_links link
    JOIN csca_classes cls ON cls.course_id = link.course_id
    WHERE cls.id = v_class_id
      AND link.source_system = 'CSCA_COURSE_LMS'
      AND link.status <> 'Pending';

    UPDATE csca_classes
    SET start_date = timestamptz '2026-11-08 00:00:00+00',
        end_date = timestamptz '2026-12-10 00:00:00+00',
        schedule = 'T3 - CN (17:00 - 19:00), T5 (18:00 - 20:00)',
        updated_at = v_now,
        updated_by = 'Codex schedule setup'
    WHERE id = v_class_id;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'Class CSCA-2026-05 was not found.';
    END IF;

    INSERT INTO csca_class_schedules
        (id, class_id, day_of_week, start_time, end_time, notes, created_at, created_by,
         status, start_date, end_date, timezone, title)
    VALUES
        (v_tuesday_id, v_class_id, 2, interval '17 hours', interval '19 hours',
         'Lịch cố định 15 buổi', v_now, 'Codex schedule setup', 'Active', date '2026-11-08', date '2026-12-10', 'Asia/Ho_Chi_Minh', 'Tiếng trung xã hội'),
        (v_thursday_id, v_class_id, 4, interval '18 hours', interval '20 hours',
         'Lịch cố định 15 buổi', v_now, 'Codex schedule setup', 'Active', date '2026-11-08', date '2026-12-10', 'Asia/Ho_Chi_Minh', 'Tiếng trung xã hội'),
        (v_sunday_id, v_class_id, 0, interval '17 hours', interval '19 hours',
         'Lịch cố định 15 buổi', v_now, 'Codex schedule setup', 'Active', date '2026-11-08', date '2026-12-10', 'Asia/Ho_Chi_Minh', 'Tiếng trung xã hội');

    FOR v_row IN
        SELECT * FROM (VALUES
            (v_tuesday_id, 2, '17:00:00', '19:00:00'),
            (v_thursday_id, 4, '18:00:00', '20:00:00'),
            (v_sunday_id, 0, '17:00:00', '19:00:00')
        ) AS slots(schedule_id, day_of_week, start_time, end_time)
    LOOP
        v_event_id := replace(gen_random_uuid()::text, '-', '');
        INSERT INTO integration_outboxes
            (id, company_id, business_unit_id, source_system, event_id, event_type,
             aggregate_type, aggregate_id, idempotency_key, correlation_id, payload_json,
             status, attempt_count, next_attempt_at, created_at, created_by)
        VALUES
            (gen_random_uuid(), v_link.company_id, v_link.business_unit_id, 'CSCA_COURSE_LMS',
             v_event_id, 'lms.management.event.requested', 'LmsCourseLink', replace(v_link.id::text, '-', ''),
             'management-event:' || v_event_id, replace(gen_random_uuid()::text, '-', ''),
             jsonb_build_object(
                 'eventId', v_event_id,
                 'eventType', 'class.schedule.upserted',
                 'occurredAt', v_now,
                 'source', 'internal-management',
                 'payload', jsonb_build_object(
                     'classSourceId', replace(v_class_id::text, '-', ''),
                     'scheduleSourceId', replace(v_row.schedule_id::text, '-', ''),
                     'lmsScheduleId', NULL,
                     'title', 'Tiếng trung xã hội',
                     'dayOfWeek', v_row.day_of_week,
                     'startTime', v_row.start_time,
                     'endTime', v_row.end_time,
                     'startDate', '2026-11-08',
                     'endDate', '2026-12-10',
                     'timezone', 'Asia/Ho_Chi_Minh',
                     'status', 'active',
                     'meetingUrl', NULL,
                     'sourceUpdatedAt', v_now
                 )
             )::text,
             'Pending', 0, v_now, v_now + (v_sequence * interval '1 millisecond'), 'Codex schedule setup');
        v_sequence := v_sequence + 1;
    END LOOP;

    FOR v_row IN
        WITH slots(schedule_id, day_of_week, start_time, end_time) AS (
            VALUES
                (v_tuesday_id, 2, interval '17 hours', interval '19 hours'),
                (v_thursday_id, 4, interval '18 hours', interval '20 hours'),
                (v_sunday_id, 0, interval '17 hours', interval '19 hours')
        )
        SELECT day::date AS lesson_date, slots.*
        FROM generate_series(date '2026-11-08', date '2026-12-10', interval '1 day') day
        JOIN slots ON slots.day_of_week = EXTRACT(DOW FROM day)::integer
        ORDER BY day, slots.start_time
        LIMIT 15
    LOOP
        v_session_id := gen_random_uuid();
        v_meeting_url := 'https://meet.jit.si/csca-' || replace(v_session_id::text, '-', '') || '-' || md5(random()::text || clock_timestamp()::text);

        INSERT INTO csca_lesson_sessions
            (id, class_id, schedule_id, lesson_date, start_time, end_time, status,
             meeting_url, notes, created_at, created_by)
        VALUES
            (v_session_id, v_class_id, v_row.schedule_id, v_row.lesson_date,
             v_row.start_time, v_row.end_time, 'Scheduled', v_meeting_url,
             'Tự động tạo từ lịch cố định 15 buổi', v_now, 'Codex schedule setup');

        v_event_id := replace(gen_random_uuid()::text, '-', '');
        INSERT INTO integration_outboxes
            (id, company_id, business_unit_id, source_system, event_id, event_type,
             aggregate_type, aggregate_id, idempotency_key, correlation_id, payload_json,
             status, attempt_count, next_attempt_at, created_at, created_by)
        VALUES
            (gen_random_uuid(), v_link.company_id, v_link.business_unit_id, 'CSCA_COURSE_LMS',
             v_event_id, 'lms.management.event.requested', 'LmsCourseLink', replace(v_link.id::text, '-', ''),
             'management-event:' || v_event_id, replace(gen_random_uuid()::text, '-', ''),
             jsonb_build_object(
                 'eventId', v_event_id,
                 'eventType', 'class.session.upserted',
                 'occurredAt', v_now,
                 'source', 'internal-management',
                 'payload', jsonb_build_object(
                     'classSourceId', replace(v_class_id::text, '-', ''),
                     'sessionSourceId', replace(v_session_id::text, '-', ''),
                     'lmsSessionId', NULL,
                     'expectedLmsVersion', NULL,
                     'scheduleSourceId', replace(v_row.schedule_id::text, '-', ''),
                     'lmsScheduleId', NULL,
                     'lessonDate', to_char(v_row.lesson_date, 'YYYY-MM-DD'),
                     'startTime', to_char(time '00:00' + v_row.start_time, 'HH24:MI:SS'),
                     'endTime', to_char(time '00:00' + v_row.end_time, 'HH24:MI:SS'),
                     'previousLessonDate', NULL,
                     'previousStartTime', NULL,
                     'previousEndTime', NULL,
                     'timezone', 'Asia/Ho_Chi_Minh',
                     'status', 'scheduled',
                     'meetingUrl', v_meeting_url,
                     'changeReason', NULL,
                     'sourceUpdatedAt', v_now
                 )
             )::text,
             'Pending', 0, v_now, v_now + ((100 + v_sequence) * interval '1 millisecond'), 'Codex schedule setup');

        v_sequence := v_sequence + 1;
        v_created := v_created + 1;
    END LOOP;

    IF v_created <> 15 THEN
        RAISE EXCEPTION 'Expected 15 sessions but created %.', v_created;
    END IF;

    UPDATE lms_course_links
    SET status = 'Processing', last_sync_error = NULL, last_synced_at = NULL,
        updated_at = v_now, updated_by = 'Codex schedule setup'
    WHERE id = v_link.id;
END $$;

COMMIT;
