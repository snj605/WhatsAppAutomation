import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { ConfirmDialogModule } from 'primeng/confirmdialog';
import { ToastModule } from 'primeng/toast';
import { IdentityDialog } from './shared/confirm-identity/identity-dialog';

/** The root component: only the page outlet plus the app-wide toast, confirm dialog and "Confirm it's you" dialog. */
@Component({
  selector: 'app-root',
  imports: [RouterOutlet, ToastModule, ConfirmDialogModule, IdentityDialog],
  templateUrl: './app.html',
})
export class App {}
