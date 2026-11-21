import type { ProblemDetails } from '../../types/api';

export class ApiClientError extends Error {
  public readonly status: number;
  public readonly errorCode: string;
  public readonly correlationId?: string;
  public readonly validationErrors?: Record<string, string[]>;

  constructor(
    status: number,
    errorCode: string,
    message: string,
    correlationId?: string,
    validationErrors?: Record<string, string[]>,
  ) {
    super(message);
    this.name = 'ApiClientError';
    this.status = status;
    this.errorCode = errorCode;
    this.correlationId = correlationId;
    this.validationErrors = validationErrors;
  }

  static fromProblemDetails(status: number, problem?: ProblemDetails | null): ApiClientError {
    const code = problem?.errorCode ?? `HTTP_${status}`;
    const message = problem?.detail ?? problem?.title ?? `Request failed with status ${status}`;
    return new ApiClientError(status, code, message, problem?.correlationId, problem?.errors);
  }
}

export function getErrorMessage(error: unknown): string {
  if (error instanceof ApiClientError) {
    return error.message;
  }
  if (error instanceof Error) {
    return error.message;
  }
  return 'Đã xảy ra lỗi không xác định khi kết nối máy chủ.';
}
