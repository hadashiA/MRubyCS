#include <mruby.h>
#include "mrubycs-compiler.h"

// for mruby-compiler2 prism_xallocator.h
mrb_state *global_mrb = NULL;

void mrubycs_compiler_prism_xallocator_init() {
  if (global_mrb == NULL) {
    global_mrb = mrb_open();    
  }  
}

void mrb_mrubycs_compiler_gem_init(mrb_state *mrb)
{
}

void mrb_mrubycs_compiler_gem_final(mrb_state *mrb)
{
  if (global_mrb != NULL) {
    mrb_close(global_mrb);
    global_mrb = NULL;
  }
}
