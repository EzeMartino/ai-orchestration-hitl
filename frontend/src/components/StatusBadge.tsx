export function StatusBadge({ status }: { status: string }) {
  return <span className={`statusBadge status-${status}`}>{status}</span>;
}
export default StatusBadge;
