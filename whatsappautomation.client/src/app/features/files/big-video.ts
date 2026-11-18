import { inject, Injectable } from '@angular/core';
import { ConfirmationService } from 'primeng/api';
import { fileSize, UploadedFile } from './files.api';

/**
 * WhatsApp plays videos up to 100 MB. A bigger one can only go as a file (people download it and open it).
 * The user is asked once per file; saying no removes the file.
 */
@Injectable({ providedIn: 'root' })
export class BigVideoConsent {
  private readonly confirm = inject(ConfirmationService);

  ask(file: UploadedFile): Promise<boolean> {
    if (!file.isBigVideo) return Promise.resolve(true);
    return new Promise<boolean>((resolve) =>
      this.confirm.confirm({
        header: 'Send this video as a file?',
        message:
          `${file.fileName} is ${fileSize(file.sizeBytes)}. WhatsApp plays videos only up to 100 MB, ` +
          'so this one goes as a file: people download it and then open it. Keep it?',
        icon: 'pi pi-video',
        acceptLabel: 'Yes, send as a file',
        rejectLabel: 'Remove it',
        rejectButtonProps: { severity: 'secondary', outlined: true },
        accept: () => resolve(true),
        reject: () => resolve(false),
      }),
    );
  }
}
