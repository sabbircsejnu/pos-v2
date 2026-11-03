// angular import
import { Component, inject, OnInit } from '@angular/core';
import { FormBuilder, FormGroup, Validators } from '@angular/forms';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';

@Component({
  selector: 'app-student-add',
  imports: [SharedModule],
  templateUrl: './student-add.component.html',
  styleUrl: './student-add.component.scss'
})
export class StudentAddComponent implements OnInit {
  private fb = inject(FormBuilder);

  basicInfoForm!: FormGroup;

  ngOnInit(): void {
    this.basicInfoForm = this.fb.group(
      {
        firstName: ['', Validators.required],
        lastName: ['', Validators.required],
        email: ['', [Validators.required, Validators.email]],
        joiningDate: ['', Validators.required],
        password: ['', [Validators.required, Validators.minLength(6)]],
        confirmPassword: ['', Validators.required],
        mobileNumber: ['', [Validators.required, Validators.pattern('^[0-9]{10}$'), Validators.minLength(10), Validators.maxLength(10)]],
        gender: ['', Validators.required],
        designation: ['', Validators.required],
        department: ['', Validators.required],
        dateOfBirth: ['', Validators.required],
        education: ['', Validators.required],
        file: [null, Validators.required]
      },
      {
        validators: this.matchPasswords // custom validator for password match
      }
    );
  }

  matchPasswords(group: FormGroup) {
    const pass = group.get('password')?.value;
    const confirmPass = group.get('confirmPassword')?.value;
    return pass === confirmPass ? null : { passwordMismatch: true };
  }

  onSubmit() {
    if (this.basicInfoForm.valid) {
      console.log(this.basicInfoForm.value);
    } else {
      this.basicInfoForm.markAllAsTouched();
    }
  }
}
