import { fileProblem, fileSize } from './files.api';

describe('fileProblem', () => {
  it('accepts documents and photos', () => {
    expect(fileProblem(new File(['x'], 'offer.pdf'))).toBeNull();
    expect(fileProblem(new File(['x'], 'photo.JPG'))).toBeNull();
  });

  it('refuses programs and empty files', () => {
    expect(fileProblem(new File(['x'], 'setup.EXE'))).toContain('Programs');
    expect(fileProblem(new File([], 'empty.pdf'))).toContain('empty');
  });
});

describe('fileSize', () => {
  it('reads like a person would say it', () => {
    expect(fileSize(500)).toBe('500 B');
    expect(fileSize(2048)).toBe('2 KB');
    expect(fileSize(3 * 1024 * 1024)).toBe('3.0 MB');
  });
});
