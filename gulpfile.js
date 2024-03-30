var gulp = require('gulp');

gulp.task('copy', function() {
    return gulp.src('node_modules/easymde/dist/*')
        .pipe(gulp.dest('wwwroot/lib/easymde/'));
});