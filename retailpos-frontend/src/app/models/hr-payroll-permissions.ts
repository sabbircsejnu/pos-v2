export const HR_PAYROLL_PERMISSION_CATEGORIES = [
  {
    name: 'HR Foundation',
    permissions: [
      'hr.dashboard.view', 'hr.employee.view', 'hr.employee.create', 'hr.employee.edit', 'hr.employee.deactivate',
      'hr.employee.view_sensitive', 'hr.employee.view_identity', 'hr.employee.link_user', 'hr.employee.unlink_user',
      'hr.employee_lifecycle.view', 'hr.employee_lifecycle.manage', 'hr.employee_lifecycle.rehire',
      'hr.employee_document.view', 'hr.employee_document.download', 'hr.employee_document.upload',
      'hr.employee_document.replace', 'hr.employee_document.delete',
      'hr.organisation.view', 'hr.organisation.manage', 'hr.position.view', 'hr.position.manage',
      'hr.location.view', 'hr.location.manage', 'hr.employee_transfer.view', 'hr.employee_transfer.create',
      'hr.employee_transfer.approve', 'hr.import.view', 'hr.import.execute',
      'hr.approval_policy.manage', 'hr.report.view', 'hr.settings.manage'
    ]
  },
  {
    name: 'HR Attendance',
    permissions: [
      'hr.shift.view', 'hr.shift.manage', 'hr.roster.view', 'hr.roster.manage',
      'hr.holiday.view', 'hr.holiday.manage', 'hr.weekly_off.view', 'hr.weekly_off.manage',
      'hr.attendance.view', 'hr.attendance.report', 'hr.attendance.manual_entry', 'hr.attendance.correct',
      'hr.attendance.approve_correction', 'hr.attendance.raw_punch.view', 'hr.attendance.reprocess',
      'hr.device.view', 'hr.device.manage', 'hr.device_user_mapping.view', 'hr.device_user_mapping.manage'
    ]
  },
  {
    name: 'HR Leave, Overtime and Exit',
    permissions: [
      'hr.leave.view', 'hr.leave.request', 'hr.leave.approve', 'hr.leave.admin',
      'hr.leave_balance.view', 'hr.leave_balance.adjust', 'hr.leave_balance.accrue',
      'hr.leave_year.close', 'hr.leave_year.reopen',
      'hr.overtime.view', 'hr.overtime.calculate', 'hr.overtime.submit', 'hr.overtime.approve',
      'hr.overtime.override', 'hr.overtime_policy.manage',
      'hr.exit.view', 'hr.exit.initiate', 'hr.exit.approve', 'hr.exit.clearance',
      'payroll.final_settlement.view', 'payroll.final_settlement.calculate',
      'payroll.final_settlement.submit', 'payroll.final_settlement.approve',
      'payroll.final_settlement.reject', 'payroll.final_settlement.cancel'
    ]
  },
  {
    name: 'Payroll Setup and Processing',
    permissions: [
      'payroll.dashboard.view', 'payroll.salary.view', 'payroll.salary.manage',
      'payroll.payment_destination.view', 'payroll.payment_destination.manage',
      'payroll.payment_destination.view_sensitive',
      'payroll.salary_setup.create', 'payroll.salary_setup.submit', 'payroll.salary_setup.approve',
      'payroll.salary_revision.create', 'payroll.salary_revision.submit', 'payroll.salary_revision.approve',
      'payroll.allowance_deduction.view', 'payroll.allowance_deduction.manage',
      'payroll.loan.view', 'payroll.loan.manage', 'payroll.loan.approve',
      'payroll.period.view', 'payroll.period.create', 'payroll.period.lock', 'payroll.period.reopen', 'payroll.period.close',
      'payroll.period.close_with_exception',
      'payroll.run.view', 'payroll.run.create', 'payroll.run.calculate', 'payroll.run.edit_draft',
      'payroll.calculation_policy.view', 'payroll.calculation_policy.manage',
      'payroll.run.submit', 'payroll.run.approve', 'payroll.run.reopen',
      'payroll.import.view', 'payroll.import.execute', 'payroll.approval_policy.manage',
      'payroll.report.view', 'payroll.settings.manage'
    ]
  },
  {
    name: 'Payroll Payments, Payslips and Accounting',
    permissions: [
      'payroll.payment.view', 'payroll.payment.view_sensitive', 'payroll.payment.process', 'payroll.payment.hold',
      'payroll.payment.release', 'payroll.payment.retry', 'payroll.payment.reconcile',
      'payroll.payslip.view', 'payroll.payslip.download', 'payroll.payslip.print', 'payroll.payslip.email',
      'payroll.export', 'payroll.bank_file.generate', 'payroll.accounting.post', 'payroll.accounting.view_posting',
      'payroll.final_settlement.financial.view', 'payroll.final_settlement.accounting.post',
      'payroll.final_settlement.payment.create', 'payroll.final_settlement.payment.view',
      'payroll.final_settlement.payment.hold', 'payroll.final_settlement.payment.release',
      'payroll.final_settlement.payment.process', 'payroll.final_settlement.payment.fail',
      'payroll.final_settlement.payment.return', 'payroll.final_settlement.payment.retry'
    ]
  }
];
