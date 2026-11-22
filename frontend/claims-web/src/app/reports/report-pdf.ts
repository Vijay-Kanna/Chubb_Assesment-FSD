import { jsPDF } from 'jspdf';
import { HookData, autoTable } from 'jspdf-autotable';
import { statusLabels } from '../core/claims';
import type { ReportsOverview } from './reports';

export function downloadReport(report: ReportsOverview) {
  const doc = new jsPDF({ putOnlyUsedFonts: true });
  const margin = 16;
  const pageWidth = doc.internal.pageSize.getWidth();
  const pageHeight = doc.internal.pageSize.getHeight();
  const range = [report.startDate, report.endDate].join(' to ');
  doc.setProperties({ title: 'Claims report', subject: 'Submission dates ' + range });
  doc.setFontSize(22).setTextColor('#24294f').text('Claims report', margin, 25);
  doc.setFontSize(10).setTextColor('#545d7a');
  doc.text('Submission dates (UTC)  ' + range, margin, 34);
  doc.text('Generated  ' + report.generatedAt.slice(0, 16).replace('T', ' ') + ' UTC', margin, 40);

  let top = 49;
  function table(title: string, columns: string[], rows: (number | string)[][]) {
    autoTable(doc, {
      startY: top,
      margin: { top: 17, right: margin, bottom: 17, left: margin },
      head: [[{ content: title, colSpan: columns.length, styles: { fillColor: '#30396d', fontSize: 11 } }], columns],
      body: rows.length ? rows : [['No records', ...columns.slice(1).map(() => '') ]],
      headStyles: { fillColor: '#46518c' },
      rowPageBreak: 'avoid',
      styles: { fontSize: 9, cellPadding: 1.7, overflow: 'linebreak' },
      didDrawPage: (data: HookData) => { top = (data.cursor?.y ?? 17) + 6; },
    });
  }

  table('Overview', ['Total submissions', 'Open claims', 'Unassigned', 'Awaiting information', 'Open without estimate'],
    [[report.totalClaims, report.openClaims, report.unassigned, report.awaitingInformation, report.missingExposure]]);
  table('Claims by current status', ['Status', 'Claims'],
    report.statuses.map(row => [statusLabels[row.status], row.count]));
  const totals = [
    ...report.requested.map(row => ({ ...row, category: 'Requested' })),
    ...report.approved.map(row => ({ ...row, category: 'Approved' })),
    ...report.exposure.map(row => ({ ...row, category: 'Outstanding exposure' })),
  ];
  table('Recorded amounts', ['Category', 'Currency', 'Amount', 'Claims'], totals.map(row =>
    [row.category, row.currency, (row.amountMinor / 100).toLocaleString('en', { minimumFractionDigits: 2, maximumFractionDigits: 2 }), row.claims]));
  table('Staff activity in range', ['Staff member', 'Current open claims', 'Decisions', 'Settlements'],
    report.staff.map(person => [person.name, person.openClaims, person.decisions, person.settlements]));

  for (let page = 1; page <= doc.getNumberOfPages(); page++) {
    doc.setPage(page);
    doc.setDrawColor('#d7dcee').line(margin, pageHeight - 14, pageWidth - margin, pageHeight - 14);
    doc.setFontSize(8).setTextColor('#545d7a');
    doc.text('Claims Workspace', margin, pageHeight - 9);
    doc.text(String(page) + ' / ' + String(doc.getNumberOfPages()), pageWidth - margin, pageHeight - 9, { align: 'right' });
  }
  doc.save('claims-report-' + report.startDate + '-to-' + report.endDate + '.pdf');
}
