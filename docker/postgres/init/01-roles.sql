\getenv api_db_password API_DB_PASSWORD
\getenv worker_db_password WORKER_DB_PASSWORD

CREATE ROLE api_user LOGIN PASSWORD :'api_db_password';
CREATE ROLE worker_user LOGIN PASSWORD :'worker_db_password';

GRANT CONNECT ON DATABASE dl_system TO api_user, worker_user;
GRANT USAGE ON SCHEMA public TO api_user, worker_user;
GRANT CREATE ON SCHEMA public TO api_user;

CREATE FUNCTION public.grant_workflow_table_privileges(table_oid oid)
RETURNS void
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog
AS $$
DECLARE
    table_name text;
    table_reference text := table_oid::regclass;
BEGIN
    SELECT relation.relname
    INTO table_name
    FROM pg_class AS relation
    WHERE relation.oid = table_oid;

    CASE table_name
        WHEN 'Applications' THEN
            EXECUTE format('GRANT SELECT, INSERT, UPDATE ON TABLE %s TO api_user', table_reference);
            EXECUTE format('GRANT SELECT ON TABLE %s TO worker_user', table_reference);
            EXECUTE format('GRANT UPDATE ("Status", "UpdatedAt", "PhotoQualityScore") ON TABLE %s TO worker_user', table_reference);
        WHEN 'AuditTrailEntry' THEN
            EXECUTE format('GRANT SELECT, INSERT, UPDATE ON TABLE %s TO api_user', table_reference);
            EXECUTE format('GRANT ALL PRIVILEGES ON TABLE %s TO worker_user', table_reference);
        WHEN 'ApplicationWorkflowState' THEN
            EXECUTE format('GRANT ALL PRIVILEGES ON TABLE %s TO worker_user', table_reference);
        WHEN 'OutboxMessage' THEN
            EXECUTE format('GRANT ALL PRIVILEGES ON TABLE %s TO api_user, worker_user', table_reference);
        WHEN 'OutboxState' THEN
            EXECUTE format('GRANT ALL PRIVILEGES ON TABLE %s TO api_user, worker_user', table_reference);
        WHEN 'InboxState' THEN
            EXECUTE format('GRANT ALL PRIVILEGES ON TABLE %s TO worker_user', table_reference);
        ELSE
            NULL;
    END CASE;
END;
$$;

REVOKE ALL ON FUNCTION public.grant_workflow_table_privileges(oid) FROM PUBLIC;

CREATE FUNCTION public.apply_workflow_table_privileges()
RETURNS event_trigger
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog
AS $$
DECLARE
    created_object record;
BEGIN
    FOR created_object IN
        SELECT command.objid
        FROM pg_event_trigger_ddl_commands() AS command
        JOIN pg_class AS relation ON relation.oid = command.objid
        WHERE command.classid = 'pg_class'::regclass
          AND command.schema_name = 'public'
          AND relation.relkind IN ('r', 'p')
    LOOP
        PERFORM public.grant_workflow_table_privileges(created_object.objid);
    END LOOP;
END;
$$;

REVOKE ALL ON FUNCTION public.apply_workflow_table_privileges() FROM PUBLIC;

CREATE EVENT TRIGGER workflow_table_permissions
    ON ddl_command_end
    WHEN TAG IN ('CREATE TABLE')
    EXECUTE FUNCTION public.apply_workflow_table_privileges();

DO $$
DECLARE
    existing_table record;
BEGIN
    FOR existing_table IN
        SELECT relation.oid
        FROM pg_class AS relation
        JOIN pg_namespace AS schema ON schema.oid = relation.relnamespace
        WHERE schema.nspname = 'public'
          AND relation.relkind IN ('r', 'p')
    LOOP
        PERFORM public.grant_workflow_table_privileges(existing_table.oid);
    END LOOP;
END;
$$;
