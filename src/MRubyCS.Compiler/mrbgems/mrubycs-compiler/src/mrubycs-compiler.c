#include <mruby.h>
#include <stdlib.h>

void mrubycs_free(void *ptr)
{
    free(ptr);
}

void mrb_mrubycs_compiler_gem_init(mrb_state *mrb)
{
}

void mrb_mrubycs_compiler_gem_final(mrb_state *mrb)
{
}
