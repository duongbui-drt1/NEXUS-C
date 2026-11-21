import type { ReactNode } from 'react';
import { ChevronLeft, ChevronRight } from 'lucide-react';
import { EmptyState, ErrorState, LoadingState } from '../feedback/Feedback';

export interface ColumnDef<T> {
  key: string;
  header: string;
  render: (row: T) => ReactNode;
  width?: string;
}

export function DataTable<T>({
  columns,
  rows,
  rowKey,
  isLoading,
  error,
  onRetry,
  emptyTitle = 'Không tìm thấy bản ghi',
  emptyDescription,
  toolbar,
  pagination,
}: {
  columns: ColumnDef<T>[];
  rows: T[];
  rowKey: (row: T) => string;
  isLoading?: boolean;
  error?: unknown;
  onRetry?: () => void;
  emptyTitle?: string;
  emptyDescription?: string;
  toolbar?: ReactNode;
  pagination?: {
    page: number;
    pageSize: number;
    totalCount: number;
    totalPages: number;
    onPageChange: (page: number) => void;
  };
}) {
  return (
    <div className="panel">
      {toolbar && <div className="table-toolbar">{toolbar}</div>}

      {isLoading ? (
        <LoadingState />
      ) : error ? (
        <div className="panel-body">
          <ErrorState error={error} onRetry={onRetry} />
        </div>
      ) : rows.length === 0 ? (
        <EmptyState title={emptyTitle} description={emptyDescription} />
      ) : (
        <>
          <div className="table-wrapper">
            <table className="data-table">
              <thead>
                <tr>
                  {columns.map((col) => (
                    <th key={col.key} style={col.width ? { width: col.width } : undefined}>
                      {col.header}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {rows.map((row) => (
                  <tr key={rowKey(row)}>
                    {columns.map((col) => (
                      <td key={col.key}>{col.render(row)}</td>
                    ))}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          {pagination && (
            <div className="table-footer">
              <span className="tabular">
                Trang {pagination.page} / {Math.max(1, pagination.totalPages)} — Tổng {pagination.totalCount} bản ghi
              </span>
              <div style={{ display: 'flex', gap: 6 }}>
                <button
                  type="button"
                  className="btn btn-sm"
                  disabled={pagination.page <= 1}
                  onClick={() => pagination.onPageChange(pagination.page - 1)}
                >
                  <ChevronLeft size={14} /> Trước
                </button>
                <button
                  type="button"
                  className="btn btn-sm"
                  disabled={pagination.page >= pagination.totalPages}
                  onClick={() => pagination.onPageChange(pagination.page + 1)}
                >
                  Sau <ChevronRight size={14} />
                </button>
              </div>
            </div>
          )}
        </>
      )}
    </div>
  );
}
